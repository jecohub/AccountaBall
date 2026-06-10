namespace AccountaBall.Core.Util;

/// Cheap deterministic task matching before falling back to the AI semantic match.
/// Port of Swift `TaskMatcher`.
public static class TaskMatcher
{
    /// Lowercase, trim whitespace, strip leading/trailing punctuation, collapse
    /// runs of spaces to one. Mirrors the Swift normalize step-for-step.
    public static string Normalize(string s)
    {
        var lowered = s.ToLowerInvariant().Trim();
        var stripped = TrimPunctuation(lowered);
        var collapsed = string.Join(' ', stripped.Split(' ', System.StringSplitOptions.RemoveEmptyEntries));
        return collapsed;
    }

    private static string TrimPunctuation(string s)
    {
        int start = 0, end = s.Length;
        while (start < end && char.IsPunctuation(s[start])) start++;
        while (end > start && char.IsPunctuation(s[end - 1])) end--;
        return s.Substring(start, end - start);
    }

    /// Returns the candidate id whose normalized title (or any normalized original)
    /// equals the normalized query; null if none.
    public static string? CheapMatch(
        string query,
        System.Collections.Generic.IReadOnlyList<(string Id, string Normalized, System.Collections.Generic.IReadOnlyList<string> Originals)> candidates)
    {
        var q = Normalize(query);
        foreach (var c in candidates)
        {
            if (c.Normalized == q) return c.Id;
            foreach (var o in c.Originals)
            {
                if (Normalize(o) == q) return c.Id;
            }
        }
        return null;
    }
}
