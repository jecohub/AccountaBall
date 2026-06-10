using System.Collections.Generic;
using System.Linq;
using AccountaBall.Core.Models;

namespace AccountaBall.Core.Util;

/// Turns per-cycle reads into the session timeline. Port of Swift `TimelineCoalescer`.
public static class TimelineCoalescer
{
    /// Ordered, consecutive-deduped labels for one task index. Off-task reads are
    /// skipped but DO break a run (so a label can repeat after an interruption).
    public static IReadOnlyList<string> LabelsForTask(int index, IReadOnlyList<(int? Index, string Label)> reads)
    {
        var outp = new List<string>();
        foreach (var (idx, label) in reads)
        {
            if (idx != index) continue;
            if ((outp.Count == 0 || outp[^1] != label) && label.Length > 0) outp.Add(label);
        }
        return outp;
    }

    public static int CycleCountForTask(int index, IReadOnlyList<(int? Index, string Label)> reads)
        => reads.Count(r => r.Index == index);

    /// Coalesce contiguous entries with the same (taskIndex, label) into ranges.
    /// The last range extends one cycleSeconds past its final entry. Entries are
    /// sorted chronologically first.
    public static IReadOnlyList<TimelineRange> Ranges(
        System.DateTimeOffset sessionStart,
        IReadOnlyList<(System.DateTimeOffset At, int? TaskIndex, string Label)> entries,
        double cycleSeconds = AppConstants.CycleSeconds)
    {
        var sorted = entries.OrderBy(e => e.At).ToList();
        var outp = new List<TimelineRange>();
        int i = 0;
        while (i < sorted.Count)
        {
            var startOff = (sorted[i].At - sessionStart).TotalSeconds;
            var idx = sorted[i].TaskIndex;
            var label = sorted[i].Label;
            int j = i;
            while (j + 1 < sorted.Count && sorted[j + 1].TaskIndex == idx && sorted[j + 1].Label == label)
            {
                j++;
            }
            var endOff = (j + 1 < sorted.Count)
                ? (sorted[j + 1].At - sessionStart).TotalSeconds
                : (sorted[j].At - sessionStart).TotalSeconds + cycleSeconds;
            outp.Add(new TimelineRange(startOff, endOff, label, idx));
            i = j + 1;
        }
        return outp;
    }
}
