using System;
using System.Collections.Generic;
using AccountaBall.Core.Models;
using AccountaBall.Core.Util;
using Xunit;

namespace AccountaBall.Core.Tests;

public class FreeBallDedupTests
{
    [Fact]
    public void NearIdentical_IsSameScreen()
    {
        var a = "Editing AppDelegate.swift func applicationDidFinishLaunching window "
              + "makeKeyAndOrderFront panel contentView NSHostingView rootView "
              + "RootCoordinatorView environmentObject state captureService ocrService";
        var aClock = a + " 10:42";   // trivial change (clock tick)
        Assert.True(FreeBallDedup.IsSameScreen(a, aClock, 0.85));

        var b = "Watching a YouTube video about basketball highlights";
        Assert.False(FreeBallDedup.IsSameScreen(a, b, 0.85));
    }

    [Fact]
    public void Similarity_Bounds()
    {
        Assert.Equal(1.0, FreeBallDedup.Similarity("foo bar baz", "foo bar baz"));
        Assert.True(FreeBallDedup.Similarity("foo bar", "nothing here") < 0.2);
        Assert.Equal(1.0, FreeBallDedup.Similarity("", ""));   // both empty
    }
}

public class FreeBallCondenserTests
{
    [Fact]
    public void Condense_TrimsUnderBudget_KeepsLongerScreenFuller()
    {
        var entries = new List<FreeBallTranscriptEntry>
        {
            new(new string('x', 500), 600),   // long-lived
            new(new string('y', 500), 30),     // brief blip
        };
        var outp = FreeBallCondenser.Condense(entries, maxChars: 300);
        var total = 0;
        foreach (var e in outp) total += e.Text.Length;
        Assert.True(total <= 300);
        Assert.Equal(2, outp.Count);
        Assert.True(outp[0].Text.Length > outp[1].Text.Length);
    }

    [Fact]
    public void Condense_UnderBudget_Unchanged()
    {
        var small = new List<FreeBallTranscriptEntry> { new("hi", 10) };
        var outp = FreeBallCondenser.Condense(small, maxChars: 1000);
        Assert.Equal(small, outp);
    }
}

public class FreeBallMarkdownTests
{
    [Fact]
    public void Render_IncludesSectionsWithItems()
    {
        var r = new FreeBallRecap(
            DateTimeOffset.FromUnixTimeSeconds(0), 600, "Mostly coded.",
            new List<CategorySpan> { new("Coding", 10) }, "Long blocks.",
            new[] { "refactor timeout" }, new[] { "Sarah (Slack)" },
            new[] { "OllamaAIService.swift" }, new[] { "reply to Sarah" }, false);
        var md = FreeBallMarkdown.Render(r);
        Assert.Contains("# FreeBall", md);
        Assert.Contains("Mostly coded.", md);
        Assert.Contains("## Working on", md);
        Assert.Contains("refactor timeout", md);
        Assert.Contains("## Open threads", md);
        Assert.Contains("reply to Sarah", md);
    }

    [Fact]
    public void Render_OmitsEmptySections()
    {
        var empty = new FreeBallRecap(
            DateTimeOffset.FromUnixTimeSeconds(0), 0, "",
            Array.Empty<CategorySpan>(), "",
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), false);
        Assert.DoesNotContain("## Working on", FreeBallMarkdown.Render(empty));
    }
}

public class FreeBallExportTests
{
    [Fact]
    public void Filename_IsMarkdownAndPrefixed()
    {
        var name = FreeBallExport.Filename(DateTimeOffset.FromUnixTimeSeconds(0));
        Assert.EndsWith(".md", name);
        Assert.StartsWith("freeball-", name);
    }
}
