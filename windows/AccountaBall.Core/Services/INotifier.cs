namespace AccountaBall.Core.Services;

/// Off-task nudge channel (macOS `NotificationService`). The Platform layer backs
/// this with Windows toasts; tests use <see cref="NullNotifier"/>.
public interface INotifier
{
    void SendOffTaskNudge(string task);
}

public sealed class NullNotifier : INotifier
{
    public void SendOffTaskNudge(string task) { }
}
