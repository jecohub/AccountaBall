using System;
using System.Threading.Tasks;
using AccountaBall.App.Services;
using AccountaBall.App.Views;
using AccountaBall.Core.Engine;
using AccountaBall.Core.Models;
using AccountaBall.Core.Services;
using AccountaBall.Core.Storage;
using AccountaBall.Core.Util;
using AccountaBall.Platform.Capture;
using AccountaBall.Platform.Services;
using AccountaBall.Platform.Storage;
using Microsoft.UI.Dispatching;

namespace AccountaBall.App.Shell;

/// Owns the engine, providers and the capture loop, and routes the view actions.
/// Port of the macOS AppDelegate wiring: build store + providers + engine, run the
/// ~3s perceive→decide cycle, and reflect AppPhase into the window size + view.
///
/// Threading: the loop runs on a DispatcherQueueTimer (UI thread). Capture/OCR/HTTP
/// are async and yield, so the UI never blocks, while the engine and the EF
/// DbContext are only ever touched from this one thread.
public sealed class AppController : IShellActions
{
    private readonly MainWindow _window;
    private readonly DispatcherQueue _dispatcher;

    private readonly AppState _state;
    private readonly AccountabilityEngine _engine;
    private readonly IScreenCapture _capture;
    private readonly IOcrService _ocr;
    private readonly IAiService _ai;
    private readonly SqliteStore _store;

    private DispatcherQueueTimer? _timer;
    private bool _busy;
    private AppPhase? _lastPhase;

    public AppController(MainWindow window)
    {
        _window = window;
        _dispatcher = window.DispatcherQueue;

        var settings = new FileKeyValueStore();
        _state = new AppState(settings);
        _store = SqliteStore.CreateDefault();
        _ai = new OllamaAiService();
        _capture = new GraphicsCaptureService();
        _ocr = new WindowsMediaOcrService();

        _engine = new AccountabilityEngine(_state, _ai, new ToastNotifier()) { Store = _store };
    }

    public void Start()
    {
        _state.LoadDriftLimit();
        _state.LoadTasks();
        _state.AppPhase = AppPhase.Welcome;

        _window.Coordinator.SetActions(this);
        RenderUi();

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(AppConstants.CycleSeconds);
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();

        // Proactive provider probe so the "needs Ollama" card shows up front
        // (mirrors the macOS launch-on-local-Ollama hint).
        _ = ProbeProviderAtStartupAsync();
    }

    private async Task ProbeProviderAtStartupAsync()
    {
        var ok = await _ai.HealthCheckAsync();
        if (!ok)
        {
            _engine.EnterAIUnavailable();
            RenderUi();
        }
    }

    // MARK: - The cycle

    private async Task TickAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_state.AppPhase == AppPhase.AiUnavailable)
            {
                await TryRecoverAsync();
                return;
            }
            if (!_state.IsCapturing) return;
            // Accountability classification only runs inside a declared session.
            // FreeBall observes without AI (full recording lands with Core M2.6).
            if (_engine.CurrentSession is null) return;

            await AccountabilityTickAsync();
        }
        catch
        {
            // A single bad cycle must never take the loop (or app) down.
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task AccountabilityTickAsync()
    {
        using var frame = await _capture.CaptureForegroundAsync();
        if (frame is null) return;

        var text = await _ocr.RecognizeTextAsync(frame.Bitmap);
        if (string.IsNullOrWhiteSpace(text)) return;

        MultiTaskResult result;
        try
        {
            var rules = _engine.AllowanceRulesByIndex(_state.Tasks);
            result = await _ai.ClassifyMultiAsync(_state.Tasks, text, rules);
        }
        catch (OperationCanceledException)
        {
            return;   // benign: we cancelled our own in-flight request
        }
        catch
        {
            _engine.EnterAIUnavailable();
            RenderUi();
            return;
        }

        _engine.ProcessCycle(result);
        RenderUi();
    }

    private async Task TryRecoverAsync()
    {
        if (await _ai.HealthCheckAsync())
        {
            _engine.RecoverFromAIUnavailable();
            RenderUi();
        }
    }

    // MARK: - Render

    private void RenderUi()
    {
        var phase = _state.AppPhase;
        if (_lastPhase != phase)
        {
            _window.Panel.ResizeFor(phase);
            _lastPhase = phase;
        }
        _window.Coordinator.Render(_state);
    }

    // MARK: - IShellActions

    public void OpenSetup()
    {
        _state.AppPhase = AppPhase.Setup;
        RenderUi();
    }

    public void AddTask(string title, string context)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        _state.Tasks.Add(new TaskItem { Task = title, Context = context });
        _state.SaveTasks();
        RenderUi();
    }

    public void RemoveTask(int index)
    {
        if (index < 0 || index >= _state.Tasks.Count) return;
        _state.Tasks.RemoveAt(index);
        _state.SaveTasks();
        RenderUi();
    }

    public void SetDriftLimit(int limit) => _state.DriftLimit = limit;

    public void StartSession()
    {
        _state.SaveTasks();
        _state.StartSession();
        _engine.BeginSession(_state.Tasks);
        RenderUi();
    }

    public void AcceptAmbiguous(string reason)
    {
        _engine.AcceptAmbiguous(reason);
        RenderUi();
    }

    public void RejectAmbiguous()
    {
        _engine.RejectAmbiguous();
        RenderUi();
    }

    public void TakeBreak()
    {
        _engine.TakeBreak();
        RenderUi();
    }

    public void ResumeWatching(bool graceForCurrentActivity)
    {
        _engine.ResumeAfterExcuse(graceForCurrentActivity);
        RenderUi();
    }

    public void DismissCompletion()
    {
        // Reset for another run: clear completion flags, back to the welcome card.
        foreach (var t in _state.Tasks) { t.IsComplete = false; t.TimeOnTask = 0; }
        _state.SessionRecap = null;
        _state.AppPhase = AppPhase.Welcome;
        _state.BallState = BallState.Idle;
        RenderUi();
    }

    public void RetryAi() => _ = TryRecoverAsync();

    public void OpenOllamaDownload() =>
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://ollama.com/download"));

    public void StartFreeBall()
    {
        _state.FreeBallStartTime = DateTimeOffset.UtcNow;
        _state.IsCapturing = true;
        _state.AppPhase = AppPhase.FreeBall;
        _state.BallState = BallState.Observing;
        RenderUi();
    }

    public void EndFreeBall()
    {
        _state.IsCapturing = false;
        _state.FreeBallSummarizing = false;
        _state.AppPhase = AppPhase.FreeBallRecap;
        _state.BallState = BallState.Idle;
        RenderUi();
    }

    public void ViewFreeBallHistory()
    {
        _state.AppPhase = AppPhase.FreeBallHistory;
        RenderUi();
    }

    public void CloseFreeBallHistory()
    {
        _state.AppPhase = AppPhase.Welcome;
        _state.BallState = BallState.Idle;
        RenderUi();
    }

    public void ExitApp()
    {
        // Stop the capture loop before tearing down so no tick fires mid-exit.
        _timer?.Stop();
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
