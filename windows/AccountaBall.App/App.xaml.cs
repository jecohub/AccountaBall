using Microsoft.UI.Xaml;

namespace AccountaBall.App;

/// Application entry. For an unpackaged WinUI 3 app the SDK generates the Main that
/// initializes the Windows App SDK bootstrapper. M4.4 replaces the placeholder
/// window with the floating panel + engine wiring.
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
