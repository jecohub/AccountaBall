using System.Collections.Generic;
using System.Threading.Tasks;
using AccountaBall.Core.Models;

namespace AccountaBall.Core.Services;

/// A prior run handed to summarizeTask for comparison (Swift's anonymous tuple).
public readonly record struct TaskPreviousRun(double DurationSeconds, IReadOnlyList<string> Steps, int OffTaskCount);

/// Shared AI provider contract. Port of the Swift `AIService` protocol — both
/// providers behave identically because they share `AiPrompts`. (The macOS-only
/// `classify` single-task method is omitted: the engine never calls it. The
/// FreeBall `summarizeFreeBall` is added with the FreeBall types in M2.6.)
public interface IAiService
{
    Task<MultiTaskResult> ClassifyMultiAsync(
        IReadOnlyList<TaskItem> tasks, string screenText,
        IReadOnlyDictionary<int, IReadOnlyList<string>> allowanceRulesByIndex);

    Task<ExcuseVerdict> EvaluateExcuseAsync(string excuse, IReadOnlyList<TaskItem> tasks, string screenText);

    Task<TaskRecap> SummarizeTaskAsync(
        string title, string context, IReadOnlyList<string> steps, double durationSeconds,
        TaskPreviousRun? previous);

    Task<(string Id, bool Confident)?> MatchTaskAsync(
        string query, IReadOnlyList<(string Id, string Title, string Summary)> candidates);

    /// Cheap reachability probe. Non-throwing by design — callers branch on the
    /// Bool, never treat a failure as off-task.
    Task<bool> HealthCheckAsync();

    /// Per-task prose for the end-of-session breakdown (local comparisons are
    /// computed by the caller; the model writes the human-readable comment).
    Task<IReadOnlyList<PerTaskComment>> SummarizeSessionAsync(IReadOnlyList<PerTaskSessionInput> perTask);

    /// Passive-mode summary (the ONLY AI call FreeBall makes, at End Session):
    /// reads this session's deduped transcript + distilled past recaps, returns a
    /// narrative + categorized time breakdown + cross-session insight + context.
    Task<FreeBallSummary> SummarizeFreeBallAsync(
        IReadOnlyList<FreeBallTranscriptEntry> transcript, IReadOnlyList<FreeBallPastRecap> pastRecaps);
}
