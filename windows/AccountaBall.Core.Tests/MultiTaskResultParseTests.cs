using AccountaBall.Core.Models;
using Xunit;

namespace AccountaBall.Core.Tests;

public class MultiTaskResultParseTests
{
    [Theory]
    [InlineData("TASK:0 | Editing the deck", "OnTask", 0, "Editing the deck")]
    [InlineData("TASK:3|x", "OnTask", 3, "x")]
    [InlineData("AMBIGUOUS | Reading docs", "Ambiguous", -1, "Reading docs")]
    [InlineData("OFFTASK | YouTube", "OffTask", -1, "YouTube")]
    [InlineData("DONE:2 | proposal sent", "Done", 2, "proposal sent")]
    [InlineData("offtask", "OffTask", -1, "")]            // case-insensitive keyword, no label
    [InlineData("AmBiGuOuS | mixed", "Ambiguous", -1, "mixed")]
    [InlineData("garbage text", "OffTask", -1, "")]       // unknown keyword -> safe default
    [InlineData("TASK:abc | x", "OffTask", -1, "x")]      // non-integer index -> falls through
    [InlineData("", "OffTask", -1, "")]                    // empty -> offTask
    [InlineData("OFFTASK | a | b", "OffTask", -1, "a | b")] // only first '|' splits; label keeps the rest
    public void Parse_MapsKeywordsAndLabels(string raw, string kind, int idx, string label)
    {
        var result = MultiTaskResult.Parse(raw);
        var (actualKind, actualIdx, actualLabel) = result switch
        {
            MultiTaskResult.OnTask o => ("OnTask", o.Index, o.Label),
            MultiTaskResult.Ambiguous a => ("Ambiguous", -1, a.Label),
            MultiTaskResult.OffTask f => ("OffTask", -1, f.Label),
            MultiTaskResult.Done d => ("Done", d.Index, d.Label),
            _ => ("?", -99, "?"),
        };
        Assert.Equal(kind, actualKind);
        Assert.Equal(idx, actualIdx);
        Assert.Equal(label, actualLabel);
    }

    [Fact]
    public void Parse_ValueEquality_Holds()
    {
        Assert.Equal(MultiTaskResult.Parse("TASK:1 | foo"), new MultiTaskResult.OnTask(1, "foo"));
        Assert.NotEqual<MultiTaskResult>(new MultiTaskResult.OffTask("x"), new MultiTaskResult.Ambiguous("x"));
    }
}
