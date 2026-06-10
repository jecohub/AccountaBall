using System.Collections.Generic;
using AccountaBall.Core.Util;
using Xunit;

namespace AccountaBall.Core.Tests;

public class TaskMatcherTests
{
    [Fact]
    public void Normalize_LowercasesTrimsStripsPunctuationCollapsesSpaces()
    {
        Assert.Equal("write the q3 proposal", TaskMatcher.Normalize("  Write the Q3 Proposal!! "));
    }

    [Fact]
    public void CheapMatch_HitsAndMisses()
    {
        var candidates = new List<(string, string, IReadOnlyList<string>)>
        {
            ("A", "write proposal", new[] { "Write proposal" }),
            ("B", "review slides", new[] { "Review slides" }),
        };
        Assert.Equal("A", TaskMatcher.CheapMatch("write proposal", candidates));   // exact normalized hit
        Assert.Equal("B", TaskMatcher.CheapMatch("review slides", candidates));     // via normalized original
        Assert.Null(TaskMatcher.CheapMatch("unrelated thing", candidates));         // no hit
    }
}
