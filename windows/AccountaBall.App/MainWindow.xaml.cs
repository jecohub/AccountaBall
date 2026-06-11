using AccountaBall.App.Shell;
using AccountaBall.Core.Models;
using Microsoft.UI.Xaml;

namespace AccountaBall.App;

/// Host window for the floating ball. Configured as the borderless, no-activate,
/// always-on-top panel via <see cref="FloatingPanel"/>; M4.3 swaps the placeholder
/// content for the per-phase views and M4.4 wires it to the engine.
public sealed partial class MainWindow : Window
{
    public FloatingPanel Panel { get; }

    public MainWindow()
    {
        this.InitializeComponent();
        Panel = new FloatingPanel(this);
        Panel.ResizeFor(AppPhase.Idle);
    }
}
