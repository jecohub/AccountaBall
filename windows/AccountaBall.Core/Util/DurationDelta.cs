namespace AccountaBall.Core.Util;

/// Local faster/slower comparison between this run and a prior duration. Port of
/// Swift `DurationDelta`. Seconds in, whole-minute delta out (rounded away from zero).
public sealed record DurationDelta(bool FasterThanPrevious, int DeltaMinutes)
{
    public static DurationDelta Compare(double current, double previous)
    {
        var deltaSec = System.Math.Abs(current - previous);
        var minutes = (int)System.Math.Round(deltaSec / 60.0, System.MidpointRounding.AwayFromZero);
        return new DurationDelta(current < previous, minutes);
    }
}
