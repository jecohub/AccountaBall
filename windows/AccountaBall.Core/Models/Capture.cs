using System;

namespace AccountaBall.Core.Models;

/// One screen read, shared by both modes (task mode no longer discards OCR).
/// Soft-referenced to its owning session by <see cref="SessionId"/> (NOT an EF
/// relationship) so a Capture can belong to either a WorkSession or a
/// FreeBallSession — a typed relationship can't span two entity types.
public sealed class Capture
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string Text { get; set; }
    public string Mode { get; set; }          // "task" | "free"
    public string? AppHint { get; set; }
    public int? TaskIndex { get; set; }
    public Guid SessionId { get; set; }

    public Capture(DateTimeOffset firstSeenAt, DateTimeOffset lastSeenAt, string text,
                   string mode, string? appHint, int? taskIndex, Guid sessionId)
    {
        FirstSeenAt = firstSeenAt; LastSeenAt = lastSeenAt; Text = text;
        Mode = mode; AppHint = appHint; TaskIndex = taskIndex; SessionId = sessionId;
    }

    public double Seconds => (LastSeenAt - FirstSeenAt).TotalSeconds;
}
