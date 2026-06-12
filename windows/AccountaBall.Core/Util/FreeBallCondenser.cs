using System;
using System.Collections.Generic;
using System.Linq;
using AccountaBall.Core.Models;

namespace AccountaBall.Core.Util;

/// Cap the transcript size fed to a local model, splitting the budget by how long
/// each screen was up so the longest-lived screens keep the most text. Order is
/// preserved. Port of Swift `FreeBallCondenser`.
public static class FreeBallCondenser
{
    private const int FloorChars = 40;

    public static IReadOnlyList<FreeBallTranscriptEntry> Condense(
        IReadOnlyList<FreeBallTranscriptEntry> entries, int maxChars = AppConstants.FreeBallMaxTranscriptChars)
    {
        var total = entries.Sum(e => e.Text.Length);
        if (total <= maxChars) return entries;

        // Reserve each entry's floor up front, then split the REMAINING budget by
        // dwell time. Reserving first keeps the grand total at or under maxChars.
        var reserved = entries.Sum(e => Math.Min(FloorChars, e.Text.Length));
        var remaining = Math.Max(0, maxChars - reserved);
        var weightTotal = entries.Sum(e => Math.Max(e.Seconds, 1));

        return entries.Select(e =>
        {
            var floor = Math.Min(FloorChars, e.Text.Length);
            var share = Math.Max(e.Seconds, 1) / weightTotal;
            var budget = floor + (int)(remaining * share);
            var trimmed = e.Text.Length <= budget ? e.Text : e.Text.Substring(0, budget);
            return new FreeBallTranscriptEntry(trimmed, e.Seconds);
        }).ToList();
    }
}
