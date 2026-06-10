using AccountaBall.Core.Util;
using Xunit;

namespace AccountaBall.Core.Tests;

public class DurationDeltaTests
{
    [Fact]
    public void Faster_WhenCurrentLessThanPrevious()
    {
        var faster = DurationDelta.Compare(current: 34 * 60, previous: 52 * 60);
        Assert.True(faster.FasterThanPrevious);
        Assert.Equal(18, faster.DeltaMinutes);
    }

    [Fact]
    public void Slower_UsesAbsoluteDelta()
    {
        var slower = DurationDelta.Compare(current: 60 * 60, previous: 50 * 60);
        Assert.False(slower.FasterThanPrevious);
        Assert.Equal(10, slower.DeltaMinutes);
    }
}
