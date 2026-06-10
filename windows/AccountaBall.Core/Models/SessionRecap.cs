namespace AccountaBall.Core.Models;

/// One contiguous block of the session timeline with the same activity label.
/// `TaskIndex == null` means off-task. Offsets are seconds from session start.
/// Port of Swift `TimelineRange`.
public sealed record TimelineRange(double StartOffset, double EndOffset, string Label, int? TaskIndex);

/// AI-written prose for one task in the end-of-session breakdown. Port of Swift `PerTaskComment`.
public sealed record PerTaskComment(string TaskTitle, string Comment, string? Suggestion);

/// One entry in the session's transparency log — a moment the ball asked or the
/// user chose. Port of Swift `CheckLogItem`. `Kind` is "ambiguous" | "offtask" | "auto-return".
public sealed record CheckLogItem(double Offset, string Kind, string Activity, string Note);

/// The full end-of-session breakdown. Port of Swift `SessionRecap`.
public sealed record SessionRecap(
    System.Collections.Generic.IReadOnlyList<TimelineRange> Ranges,
    System.Collections.Generic.IReadOnlyList<PerTaskComment> PerTask,
    System.Collections.Generic.IReadOnlyList<CheckLogItem> Checks,
    int DriftCount,
    int DriftLimit,
    bool CommitmentBroken);

/// Input to the AI's summarizeSession — one per task. The local comparison string
/// is authoritative; the AI writes the human comment. Port of Swift `PerTaskSessionInput`.
public sealed record PerTaskSessionInput(
    string Title,
    double DurationSeconds,
    double? LastDurationSeconds,
    double? AverageSeconds,
    int OffTaskCount,
    System.Collections.Generic.IReadOnlyList<string> Steps,
    string LocalComparison);
