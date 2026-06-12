using AccountaBall.Core.Services;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AccountaBall.App.Services;

/// <see cref="INotifier"/> backed by Windows toasts (the macOS NotificationService
/// analog). Lives in the App project because it needs WindowsAppSDK, which the
/// packaged App owns; <see cref="App"/> calls
/// <c>AppNotificationManager.Default.Register()</c> once at startup so these
/// appear.
public sealed class ToastNotifier : INotifier
{
    public void SendOffTaskNudge(string task)
    {
        var body = string.IsNullOrWhiteSpace(task)
            ? "You've drifted off task."
            : $"You've drifted from: {task}";

        var notification = new AppNotificationBuilder()
            .AddText("AccountaBall")
            .AddText(body)
            .BuildNotification();

        AppNotificationManager.Default.Show(notification);
    }
}
