using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AccountaBall.Core.Util;

/// Decide whether two consecutive OCR reads show effectively the same screen, so
/// adjacent near-identical cycles collapse into one block. Port of Swift `FreeBallDedup`.
public static class FreeBallDedup
{
    /// Jaccard similarity over lowercased word sets. 1.0 = identical sets, 0 = disjoint.
    public static double Similarity(string a, string b)
    {
        var sa = Tokens(a).ToHashSet();
        var sb = Tokens(b).ToHashSet();
        if (sa.Count == 0 && sb.Count == 0) return 1.0;
        var inter = sa.Intersect(sb).Count();
        var union = sa.Union(sb).Count();
        return union == 0 ? 0 : (double)inter / union;
    }

    public static bool IsSameScreen(string a, string b, double threshold = AppConstants.FreeBallDedupThreshold)
        => Similarity(a, b) >= threshold;

    private static readonly Regex NonAlphanumeric = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    private static IEnumerable<string> Tokens(string s)
        => NonAlphanumeric.Split(s.ToLowerInvariant()).Where(t => t.Length > 0);
}
