using AccountaBall.App.Shell;
using AccountaBall.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AccountaBall.App.Views;

/// Selects the view for the current <see cref="AppPhase"/> — the ball for compact
/// phases, the matching card otherwise. Port of the macOS RootCoordinator /
/// content switch. Views are created once and re-bound each render.
public sealed partial class RootCoordinator : UserControl
{
    private IShellActions _actions = NullShellActions.Instance;

    // Lazily-built card views, reused across renders.
    private WelcomeView? _welcome;
    private TaskSetupView? _setup;
    private WhatsUpView? _whatsUp;
    private AmbiguousView? _ambiguous;
    private OffTaskView? _offTask;
    private ProgressView? _progress;
    private CompletionView? _completion;
    private AiUnavailableView? _aiUnavailable;
    private FreeBallCardView? _freeBallCard;

    public RootCoordinator()
    {
        this.InitializeComponent();
        CloseButton.Click += (_, _) => _actions.ExitApp();
        Ball.Tapped += (_, _) => _actions.BallTapped();
    }

    public void SetActions(IShellActions actions) => _actions = actions;

    /// Wire window dragging: grabbing the ball or the card body repositions the
    /// floating panel (button clicks / the ball tap still work — see WindowDrag).
    public void AttachWindow(Shell.FloatingPanel panel)
    {
        Shell.WindowDrag.Attach(Ball, panel);
        Shell.WindowDrag.Attach(CardHost, panel);
    }

    /// Render the shell for the given state. Idempotent — safe to call every cycle.
    public void Render(AppState state)
    {
        Ball.SetState(state.BallState);

        if (IsCompact(state.AppPhase))
        {
            Ball.Visibility = Visibility.Visible;
            CardHost.Content = null;
            CardHost.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Collapsed;   // would be clipped by the round ball
            return;
        }

        Ball.Visibility = Visibility.Collapsed;
        CardHost.Visibility = Visibility.Visible;
        CardHost.Content = CardFor(state);
        CloseButton.Visibility = Visibility.Visible;
    }

    private UIElement CardFor(AppState state) => state.AppPhase switch
    {
        AppPhase.Welcome => Bind(_welcome ??= new WelcomeView(), state),
        AppPhase.Setup => Bind(_setup ??= new TaskSetupView(), state),
        AppPhase.WhatsUp => Bind(_whatsUp ??= new WhatsUpView(), state),
        AppPhase.Ambiguous => Bind(_ambiguous ??= new AmbiguousView(), state),
        AppPhase.OffTask => Bind(_offTask ??= new OffTaskView(), state),
        AppPhase.Progress => Bind(_progress ??= new ProgressView(), state),
        AppPhase.Complete => Bind(_completion ??= new CompletionView(), state),
        AppPhase.AiUnavailable => Bind(_aiUnavailable ??= new AiUnavailableView(), state),
        AppPhase.FreeBallLog or AppPhase.FreeBallRecap or AppPhase.FreeBallHistory
            => Bind(_freeBallCard ??= new FreeBallCardView(), state),
        _ => Bind(_welcome ??= new WelcomeView(), state),
    };

    private UIElement Bind(IPhaseView view, AppState state)
    {
        view.Bind(state, _actions);
        return (UIElement)view;
    }

    /// Phases that render as the round ball rather than a card.
    private static bool IsCompact(AppPhase phase) =>
        phase is AppPhase.Idle or AppPhase.Session or AppPhase.FreeBall;
}

/// Implemented by every phase card so the coordinator can bind uniformly.
public interface IPhaseView
{
    void Bind(AppState state, IShellActions actions);
}

/// No-op actions used before the host wires the real ones (M4.4).
internal sealed class NullShellActions : IShellActions
{
    public static readonly NullShellActions Instance = new();
    public void OpenSetup() { }
    public void AddTask(string title, string context) { }
    public void RemoveTask(int index) { }
    public void SetDriftLimit(int limit) { }
    public void StartSession() { }
    public void EndSession() { }
    public void AcceptAmbiguous(string reason) { }
    public void RejectAmbiguous() { }
    public void TakeBreak() { }
    public void ResumeWatching(bool graceForCurrentActivity) { }
    public void DismissCompletion() { }
    public void RetryAi() { }
    public void OpenOllamaDownload() { }
    public void StartFreeBall() { }
    public void EndFreeBall() { }
    public void ViewFreeBallHistory() { }
    public void CloseFreeBallHistory() { }
    public System.Collections.Generic.IReadOnlyList<HistoryEntry> History()
        => System.Array.Empty<HistoryEntry>();
    public void BallTapped() { }
    public void ExitApp() { }
}
