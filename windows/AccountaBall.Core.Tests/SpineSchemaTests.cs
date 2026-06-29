using System;
using System.Linq;
using AccountaBall.Core.Models;
using AccountaBall.Core.Storage;
using Xunit;

namespace AccountaBall.Core.Tests;

public class SpineSchemaTests
{
    [Fact]
    public void Capture_RoundTrips_AndLatestForSessionQuery()
    {
        var store = new InMemoryStore();
        var sid = Guid.NewGuid();
        var t0 = DateTimeOffset.UnixEpoch;
        store.AddCapture(new Capture(t0, t0, "screen A", "task", "Notepad", 0, sid));
        store.AddCapture(new Capture(t0.AddSeconds(5), t0.AddSeconds(5), "screen B", "task", null, 0, sid));
        store.AddCapture(new Capture(t0, t0, "other session", "task", null, null, Guid.NewGuid()));
        store.Save();

        var latest = store.LatestCaptureForSession(sid);
        Assert.NotNull(latest);
        Assert.Equal("screen B", latest!.Text);
        Assert.Equal("task", latest.Mode);
        Assert.Equal(2, store.Captures.Count(c => c.SessionId == sid));
    }
}
