using AccountaBall.Core.Util;
using Xunit;

namespace AccountaBall.Core.Tests;

public class AppConstantsTests
{
    [Fact]
    public void CadenceAndWindows_MatchMacOS()
    {
        Assert.Equal(3, AppConstants.CycleSeconds);
        Assert.Equal(15, AppConstants.DriftConfirmSeconds);
        Assert.Equal(300, AppConstants.BreakSeconds);
        Assert.Equal(120, AppConstants.ContinueAnywayGraceSeconds);
        Assert.Equal(500, AppConstants.PeripheralScreenChars);
    }

    [Fact]
    public void FreeBallTuning_MatchesMacOS()
    {
        Assert.Equal(0.85, AppConstants.FreeBallDedupThreshold, 3);
        Assert.Equal(28000, AppConstants.FreeBallMaxTranscriptChars);
        Assert.Equal(40, AppConstants.FreeBallMinCharsToSummarize);
        Assert.Equal(10, AppConstants.FreeBallPastRecapCap);
        Assert.Equal(120, AppConstants.FreeBallSummarizeTimeout);
    }
}
