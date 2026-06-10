using System;
using System.Collections.Generic;
using AccountaBall.Core.Models;
using AccountaBall.Core.Util;
using Xunit;

namespace AccountaBall.Core.Tests;

public class TimelineCoalescerTests
{
    [Fact]
    public void LabelsForTask_DedupesConsecutive_SkipsOffTask_KeepsOrder()
    {
        var reads = new List<(int?, string)>
        {
            (0, "editing AppDelegate"), (0, "editing AppDelegate"), (0, "running tests"),
            (null, "twitter"), (0, "editing Engine"),
        };
        var steps = TimelineCoalescer.LabelsForTask(0, reads);
        Assert.Equal(new[] { "editing AppDelegate", "running tests", "editing Engine" }, steps);
        Assert.Equal(4, TimelineCoalescer.CycleCountForTask(0, reads));
    }

    private static (DateTimeOffset, int?, string) E(DateTimeOffset start, double s, int? idx, string l)
        => (start.AddSeconds(s), idx, l);

    [Fact]
    public void Ranges_CoalescesContiguousSameLabel()
    {
        var start = DateTimeOffset.UtcNow;
        var entries = new List<(DateTimeOffset, int?, string)>
        {
            E(start, 0, 0, "vscode"), E(start, 5, 0, "vscode"),
            E(start, 10, null, "linkedin"), E(start, 15, null, "linkedin"),
        };
        var ranges = TimelineCoalescer.Ranges(start, entries, cycleSeconds: 5);
        Assert.Equal(2, ranges.Count);
        Assert.Equal(new TimelineRange(0, 10, "vscode", 0), ranges[0]);
        Assert.Equal(new TimelineRange(10, 20, "linkedin", null), ranges[1]);
    }

    [Fact]
    public void Ranges_SingleEntry_ExtendsByCycle()
    {
        var start = DateTimeOffset.UtcNow;
        var entries = new List<(DateTimeOffset, int?, string)> { (start, 0, "code") };
        var ranges = TimelineCoalescer.Ranges(start, entries, cycleSeconds: 5);
        Assert.Single(ranges);
        Assert.Equal(0, ranges[0].StartOffset);
        Assert.Equal(5, ranges[0].EndOffset);
    }

    [Fact]
    public void Ranges_Empty_YieldsEmpty()
    {
        var ranges = TimelineCoalescer.Ranges(DateTimeOffset.UtcNow,
            new List<(DateTimeOffset, int?, string)>(), cycleSeconds: 5);
        Assert.Empty(ranges);
    }

    [Fact]
    public void Ranges_InterleavedTasks_NoAdjacentMerge()
    {
        var start = DateTimeOffset.UtcNow;
        var entries = new List<(DateTimeOffset, int?, string)>
        {
            E(start, 0, 0, "vscode"), E(start, 5, 1, "slides"),
            E(start, 10, 0, "vscode"), E(start, 15, null, "yt"),
        };
        var ranges = TimelineCoalescer.Ranges(start, entries, cycleSeconds: 5);
        Assert.Equal(4, ranges.Count);
        Assert.Equal(0, ranges[0].TaskIndex);
        Assert.Equal(1, ranges[1].TaskIndex);
        Assert.Equal(0, ranges[2].TaskIndex);
        Assert.Null(ranges[3].TaskIndex);
    }
}
