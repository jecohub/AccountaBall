namespace AccountaBall.Core.Models;

/// A completed task run. Port of Swift `TaskSession`. `Duration` is the seconds
/// between start and completion.
public sealed record TaskSession(string Task, System.DateTimeOffset StartedAt, System.DateTimeOffset CompletedAt)
{
    public double Duration => (CompletedAt - StartedAt).TotalSeconds;
}
