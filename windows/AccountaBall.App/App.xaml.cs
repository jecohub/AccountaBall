using System;
using System.IO;
using AccountaBall.App.Shell;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

namespace AccountaBall.App;

/// Application entry. For an unpackaged WinUI 3 app the SDK generates the Main that
/// initializes the Windows App SDK bootstrapper. Registers the notification manager
/// (so off-task toasts appear) and starts the engine/capture loop via
/// <see cref="AppController"/>.
public partial class App : Application
{
    private Window? _window;
    private AppController? _controller;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += (_, e) =>
        {
            Log($"UnhandledException: {e.Message}\n{e.Exception}");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Toasts are non-critical: an unpackaged app may not be able to
            // register the COM activator, and that must not block the shell.
            try { AppNotificationManager.Default.Register(); }
            catch (Exception ex) { Log($"AppNotification Register failed (non-fatal): {ex.Message}"); }

            var window = new MainWindow();
            _window = window;
            _controller = new AppController(window);

            window.Activate();
            _controller.Start();
        }
        catch (Exception ex)
        {
            Log($"OnLaunched fatal: {ex}");
            throw;
        }
    }

    /// Best-effort crash log to %LOCALAPPDATA%\AccountaBall\logs\app.log (falls back
    /// to the temp dir if that path isn't writable yet).
    internal static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AccountaBall", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "app.log"),
                $"{DateTimeOffset.Now:o}  {message}{Environment.NewLine}");
        }
        catch
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "accountaball.log"),
                $"{DateTimeOffset.Now:o}  {message}{Environment.NewLine}"); }
            catch { /* give up silently */ }
        }
    }
}
