using AccountaBall.App.Shell;
using AccountaBall.App.Views;
using AccountaBall.Core.Models;
using Microsoft.UI.Xaml;

namespace AccountaBall.App;

/// Host window for the floating ball. Configured as the borderless, no-activate,
/// always-on-top panel via <see cref="FloatingPanel"/>; hosts the
/// <see cref="RootCoordinator"/> that renders the per-phase view. M4.4 wires it to
/// the engine.
public sealed partial class MainWindow : Window
{
    public FloatingPanel Panel { get; }
    public RootCoordinator Coordinator => Root;

    public MainWindow()
    {
        this.InitializeComponent();
        Panel = new FloatingPanel(this);
        Panel.ResizeFor(AppPhase.Idle);
        Root.AttachWindow(Panel);
    }
}
