using System.Linq;

namespace AccountaBall.Core.Models;

/// The verdict on a typed off-task excuse. Port of Swift `ExcuseVerdict`.
public sealed record ExcuseVerdict(bool Justified, int? TaskIndex, string Rule)
{
    /// Accepts "VERDICT | taskIndex | rule". Reject "NOT" before matching
    /// "JUSTIFIED" (so "NOT_JUSTIFIED" doesn't false-positive on the substring).
    /// The rule is kept on BOTH verdicts (it's the "why" we show); only the task
    /// attribution is dropped when not justified.
    public static ExcuseVerdict Parse(string raw)
    {
        var parts = (raw ?? string.Empty).Split('|').Select(p => p.Trim()).ToArray();
        var verdict = (parts.Length > 0 ? parts[0] : string.Empty).ToUpperInvariant();
        var justified = !verdict.Contains("NOT") && verdict.Contains("JUSTIFIED");
        int? taskIndex = parts.Length > 1 && int.TryParse(parts[1], out var ti) ? ti : null;
        var rule = parts.Length > 2 ? parts[2] : string.Empty;
        return new ExcuseVerdict(justified, justified ? taskIndex : null, rule);
    }
}
