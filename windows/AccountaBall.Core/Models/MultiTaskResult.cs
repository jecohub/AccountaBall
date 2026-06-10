namespace AccountaBall.Core.Models;

/// The model's per-cycle perception. Faithful port of Swift `MultiTaskResult`
/// (src/Sources/AccountaBall/Models/MultiTaskResult.swift). A discriminated
/// hierarchy of records gives the same exhaustive pattern-matching and value
/// equality the Swift enum had.
public abstract record MultiTaskResult
{
    public sealed record OnTask(int Index, string Label) : MultiTaskResult;
    public sealed record Ambiguous(string Label) : MultiTaskResult;   // model genuinely can't tell
    public sealed record OffTask(string Label) : MultiTaskResult;
    public sealed record Done(int Index, string Label) : MultiTaskResult;

    /// Accepts "RESULT | label". Label is optional. Keyword is case-insensitive;
    /// label case is preserved. Unknown keywords fall back to OffTask (the Swift
    /// default), so a garbled model line is never mistaken for on-task.
    public static MultiTaskResult Parse(string raw)
    {
        // Split on the first '|' only, keeping empty trailing parts (mirrors Swift's
        // maxSplits: 1, omittingEmptySubsequences: false).
        var parts = (raw ?? string.Empty).Split('|', 2);
        var keyword = parts[0].Trim().ToUpperInvariant();
        var label = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        if (keyword == "AMBIGUOUS") return new Ambiguous(label);
        if (keyword == "OFFTASK") return new OffTask(label);
        if (keyword.StartsWith("TASK:", System.StringComparison.Ordinal)
            && int.TryParse(keyword.AsSpan(5), out var taskIdx))
        {
            return new OnTask(taskIdx, label);
        }
        if (keyword.StartsWith("DONE:", System.StringComparison.Ordinal)
            && int.TryParse(keyword.AsSpan(5), out var doneIdx))
        {
            return new Done(doneIdx, label);
        }
        return new OffTask(label);
    }
}
