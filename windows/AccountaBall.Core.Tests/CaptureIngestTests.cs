using System;
using System.Linq;
using AccountaBall.Core.Models;
using AccountaBall.Core.Storage;
using Xunit;

namespace AccountaBall.Core.Tests;

public class CaptureIngestTests
{
    [Fact]
    public void IngestCapture_DedupsAndExtends_TaggedTask()
    {
        var (state, engine, store, clock) = EngineTest.MakeWithClock(null, "Ship the port");
        clock.Now = DateTimeOffset.UnixEpoch;
        engine.IngestCapture("editing AppController.cs line 1");
        clock.Now = clock.Now.AddSeconds(30);
        engine.IngestCapture("editing AppController.cs line 1 ");      // same -> extend
        clock.Now = clock.Now.AddSeconds(30);
        engine.IngestCapture("watching youtube basketball video");     // new -> insert

        var caps = store.Captures.Where(c => c.SessionId == engine.CurrentSession!.Id).ToList();
        Assert.Equal(2, caps.Count);
        Assert.All(caps, c => Assert.Equal("task", c.Mode));
        var first = caps.Single(c => c.Text.StartsWith("editing"));
        Assert.True(first.LastSeenAt > first.FirstSeenAt);            // extend branch ran
        Assert.True(first.Seconds >= 30);
    }
}
