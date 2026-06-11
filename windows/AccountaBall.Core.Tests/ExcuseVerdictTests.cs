using AccountaBall.Core.Models;
using Xunit;

namespace AccountaBall.Core.Tests;

public class ExcuseVerdictTests
{
    [Fact]
    public void Parse_Justified_WithIndexAndRule()
    {
        var j = ExcuseVerdict.Parse("JUSTIFIED | 2 | watching React tutorials");
        Assert.True(j.Justified);
        Assert.Equal(2, j.TaskIndex);
        Assert.Equal("watching React tutorials", j.Rule);
    }

    [Fact]
    public void Parse_NotJustified_EmptyFields()
    {
        var n = ExcuseVerdict.Parse("NOT_JUSTIFIED | |");
        Assert.False(n.Justified);
        Assert.Null(n.TaskIndex);
        Assert.Equal("", n.Rule);
    }

    [Fact]
    public void Parse_Rejected_KeepsRuleDropsIndex()
    {
        var nr = ExcuseVerdict.Parse("NOT_JUSTIFIED | | social media");
        Assert.False(nr.Justified);
        Assert.Null(nr.TaskIndex);
        Assert.Equal("social media", nr.Rule);
    }

    [Fact]
    public void Parse_SubstringGuard_AndDefaults()
    {
        Assert.False(ExcuseVerdict.Parse("not justified").Justified);   // contains JUSTIFIED but also NOT
        Assert.False(ExcuseVerdict.Parse("garbage").Justified);
        Assert.Null(ExcuseVerdict.Parse("JUSTIFIED").TaskIndex);        // missing index tolerated
    }
}
