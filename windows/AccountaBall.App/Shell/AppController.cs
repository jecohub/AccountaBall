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
    private readonly FreeBallEngine _freeBall;
    private readonly IScreenCapture _capture;
    private readonly IOcrService _ocr;
    private readonly IAiService _ai;
    private readonly SqliteStore _store;

    private DispatcherQueueTimer? _timer;
    private bool _busy;
    private bool _recapFinalized;
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
        _freeBall = new FreeBallEngine(_state, _ai) { Store = _store };
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
        App.Log($"controller started: capture loop @ {AppConstants.CycleSeconds}s cadence");

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
            // FreeBall passive mode: record every cycle, run NO AI until End Session.
            if (_freeBall.CurrentSession is not null)
            {
                await FreeBallTickAsync();
                return;
            }
            // Accountability classification only runs inside a declared session.
            if (_engine.CurrentSession is null) return;

            await AccountabilityTickAsync();
        }
        catch (Exception ex)
        {
            // A single bad cycle must never take the loop (or app) down — but log
            // it, otherwise a broken capture/OCR/classify stage is invisible.
            App.Log($"tick error: {ex}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task AccountabilityTickAsync()
    {
        using var frame = await _capture.CaptureForegroundAsync();
        if (frame is null)
        {
            App.Log("cycle: capture returned null (no frame) — skipping");
            return;
        }

        var text = await _ocr.RecognizeTextAsync(frame.Bitmap);
        if (string.IsNullOrWhiteSpace(text))
        {
            App.Log($"cycle: OCR empty for window '{frame.WindowTitle}' — skipping");
            return;
        }

        App.Log($"cycle: window='{frame.WindowTitle}' ocrChars={text.Length} text[0..120]=\"{Truncate(text, 120)}\"");

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
        catch (Exception ex)
        {
            App.Log($"cycle: classify failed -> AiUnavailable: {ex.Message}");
            _engine.EnterAIUnavailable();
            RenderUi();
            return;
        }

        _engine.ProcessCycle(result);
        App.Log($"cycle: classify={result.GetType().Name} label=\"{ResultLabel(result)}\" -> phase={_state.AppPhase} ball={_state.BallState}");

        // A DONE read that finishes the last task flips the phase to Complete
        // (AppState.CompleteTaskAt). Build the recap before the engine session is
        // closed, mirroring the macOS phase-watcher's finalizeSessionRecap call.
        if (_state.AppPhase == AppPhase.Complete && !_recapFinalized)
        {
            await FinalizeAndCloseAsync();
            _state.BallState = BallState.Done;
        }
        RenderUi();
    }

    /// FreeBall cycle: capture the focused window, OCR it, and hand the text to the
    /// engine to dedup + store. Deliberately no AI here — passive mode makes its one
    /// and only model call at End Session (<see cref="EndFreeBall"/>).
    private async Task FreeBallTickAsync()
    {
        using var frame = await _capture.CaptureForegroundAsync();
        if (frame is null) { App.Log("freeball: capture returned null — skipping"); return; }

        var text = await _ocr.RecognizeTextAsync(frame.Bitmap);
        if (string.IsNullOrWhiteSpace(text))
        {
            App.Log($"freeball: OCR empty for '{frame.WindowTitle}' — skipping");
            return;
        }

        _freeBall.Ingest(text);
        App.Log($"freeball: ingest window='{frame.WindowTitle}' chars={text.Length} cycles={_freeBall.CurrentSession?.CycleCount}");
    }

    /// Build the recap (while the engine's WorkSession is still alive) then close
    /// that session. Idempotent via <see cref="_recapFinalized"/> so the DONE path
    /// and a manual end can't double-finalize.
    private async Task FinalizeAndCloseAsync()
    {
        if (_recapFinalized) return;
        _recapFinalized = true;
        try { await _engine.FinalizeSessionRecapAsync(); }
        catch (Exception ex) { App.Log($"finalize recap error: {ex.Message}"); }
        _engine.EndSession();
    }

    /// The AI's activity label for this read — surfaced in the cycle log because the
    /// off-task dwell streak keys on it (a label that changes each cycle restarts the
    /// 15s drift timer, so a screen can read OffTask forever yet never confirm).
    private static string ResultLabel(MultiTaskResult r) => r switch
    {
        MultiTaskResult.OnTask o => o.Label,
        MultiTaskResult.OffTask o => o.Label,
        MultiTaskResult.Ambiguous a => a.Label,
        MultiTaskResult.Done d => d.Label,
        _ => string.Empty,
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s.Replace("\r", " ").Replace("\n", " ")
                        : s.Substring(0, max).Replace("\r", " ").Replace("\n", " ") + "…";

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
        _recapFinalized = false;
        App.Log($"session started: {_state.Tasks.Count} task(s), driftLimit={_state.DriftLimit}");
        RenderUi();
    }

    public async void EndSession()
    {
        // Manual end (from the Progress card). Freeze the duration before
        // SessionStartTime is nilled, build the recap while the engine's
        // WorkSession is still alive (M2.5), then show the Completion card.
        if (_state.SessionStartTime is { } start)
            _state.LastSessionDuration = (DateTimeOffset.UtcNow - start).TotalSeconds;
        await FinalizeAndCloseAsync();   // builds SessionRecap, then closes the engine session
        _state.EndSession();
        _state.BallState = BallState.Done;
        _state.AppPhase = AppPhase.Complete;
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
        _freeBall.Begin();                   // opens the session record + flips to FreeBall phase
        _state.IsCapturing = true;
        _state.BallState = BallState.Observing;
        App.Log("freeball: session started");
        RenderUi();
    }

    public async void EndFreeBall()
    {
        // Stop the capture loop, then run the single end-of-session summarize. EndAsync
        // synchronously flips to the FreeBallRecap "Summarizing…" state before its AI
        // await, so render once to show the loading card, then again with the result.
        _state.IsCapturing = false;
        _state.BallState = BallState.Idle;
        App.Log("freeball: ending — summarizing");
        try
        {
            var pending = _freeBall.EndAsync();
            RenderUi();
            await pending;
            App.Log("freeball: recap ready");
        }
        catch (Exception ex) { App.Log($"freeball end error: {ex.Message}"); }
        RenderUi();
    }

    public System.Collections.Generic.IReadOnlyList<HistoryEntry> History()
    {
        var outp = new System.Collections.Generic.List<HistoryEntry>();

        // FreeBall runs — the AI narrative is the summary.
        foreach (var s in _store.FreeBallSessions)
        {
            if (s.EndedAt is null) continue;
            var summary = !string.IsNullOrWhiteSpace(s.Narrative) ? s.Narrative
                        : s.RecapPending ? "(summary pending — AI was unreachable)"
                        : "(no summary)";
            outp.Add(new HistoryEntry("FreeBall", s.EndedAt.Value, summary, s.RecapPending));
        }

        // Declared sessions — no stored narrative, so derive a one-liner from the
        // persisted timeline (tasks worked, wall-clock minutes, off-task check count).
        foreach (var s in _store.Sessions)
        {
            if (s.EndedAt is null) continue;
            var tasks = s.TaskTitles.Count > 0 ? string.Join(", ", s.TaskTitles) : "Untitled session";
            int mins = (int)System.Math.Round((s.EndedAt.Value - s.StartedAt).TotalMinutes);
            int offTask = 0;
            foreach (var j in s.Justifications) if (j.Kind == "offtask") offTask++;
            outp.Add(new HistoryEntry("Session", s.EndedAt.Value,
                $"{tasks} · {mins} min · {offTask} off-task", false));
        }

        outp.Sort((a, b) => b.Date.CompareTo(a.Date));   // newest first
        return outp;
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

    public void BallTapped()
    {
        // The compact ball is just a status indicator — tapping it opens the card
        // where the user can actually act (end FreeBall / see session progress).
        switch (_state.AppPhase)
        {
            case AppPhase.FreeBall:
                _state.AppPhase = AppPhase.FreeBallLog;
                RenderUi();
                break;
            case AppPhase.Session:
                _state.AppPhase = AppPhase.Progress;
                RenderUi();
                break;
        }
    }

    public void ExitApp()
    {
        // Stop the capture loop before tearing down so no tick fires mid-exit.
        _timer?.Stop();
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
