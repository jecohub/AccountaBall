using System;
using System.Collections.Generic;
using AccountaBall.Core.Models;
using AccountaBall.Core.Util;
using Xunit;

namespace AccountaBall.Core.Tests;

public class FreeBallSummarizeTests
{
    [Fact]
    public void ParseFreeBallSummary_GoodJson()
    {
        var json = "{\"narrative\":\"You mostly coded.\",\"categories\":[{\"label\":\"Coding\",\"minutes\":45},{\"label\":\"Email\",\"minutes\":10}],"
                 + "\"insight\":\"You code in long blocks.\",\"workingOn\":[\"refactoring the timeout handling\"],"
                 + "\"people\":[\"Sarah (Slack) — launch Friday\"],\"codeContext\":[\"OllamaAIService.swift\"],\"openThreads\":[\"reply to Sarah\"]}";
        var outp = AiPrompts.ParseFreeBallSummary(json);
        Assert.Equal("You mostly coded.", outp.Narrative);
        Assert.Equal(2, outp.Categories.Count);
        Assert.Equal("Coding", outp.Categories[0].Label);
        Assert.Equal(45, outp.Categories[0].Minutes);
        Assert.Equal("You code in long blocks.", outp.Insight);
        Assert.Equal(new[] { "refactoring the timeout handling" }, outp.WorkingOn);
        Assert.Equal("Sarah (Slack) — launch Friday", outp.People[0]);
        Assert.Equal(new[] { "OllamaAIService.swift" }, outp.CodeContext);
        Assert.Equal(new[] { "reply to Sarah" }, outp.OpenThreads);
    }

    [Fact]
    public void ParseFreeBallSummary_BadJson_EmptySummary()
    {
        var outp = AiPrompts.ParseFreeBallSummary("not json");
        Assert.Empty(outp.Narrative);
        Assert.Empty(outp.Categories);
        Assert.Empty(outp.WorkingOn);
    }

    [Fact]
    public void BuildFreeBallPrompt_IncludesTranscriptAndPast()
    {
        var transcript = new List<FreeBallTranscriptEntry> { new("editing main.swift", 120) };
        var past = new List<FreeBallPastRecap>
        {
            new("Lots of email.", new List<CategorySpan> { new("Email", 30) }, "Mornings are emaily.", new[] { "reply to vendor" }),
        };
        var prompt = AiPrompts.BuildFreeBallPrompt(transcript, past);
        Assert.Contains("editing main.swift", prompt);
        Assert.Contains("Lots of email.", prompt);
        Assert.Contains("reply to vendor", prompt);
    }

    [Fact]
    public void FreeBallRecap_FromSession()
    {
        var s = new FreeBallSession(DateTimeOffset.FromUnixTimeSeconds(1000))
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(1600),   // 600s
            Narrative = "did stuff",
            WorkingOn = { "W" },
            OpenThreads = { "O" },
        };
        var r = FreeBallRecap.FromSession(s);
        Assert.Equal(600, r.Duration);
        Assert.Equal("did stuff", r.Narrative);
        Assert.Equal(new[] { "W" }, r.WorkingOn);
        Assert.Equal(new[] { "O" }, r.OpenThreads);
        Assert.False(r.RecapPending);
    }
}
