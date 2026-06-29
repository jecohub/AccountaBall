using System;
using System.Linq;
using AccountaBall.Core.Models;
using AccountaBall.Core.Storage;
using Xunit;
// Disambiguate the spine `Thread` from System.Threading.Thread (pulled in by
// ImplicitUsings) so the bare `Thread` in the test refers to the model.
using Thread = AccountaBall.Core.Models.Thread;

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

    [Fact]
    public void Project_OwnsThreads_AndPersistsViaStore()
    {
        var store = new InMemoryStore();
        var project = new Project("Windows port");
        project.Threads.Add(new Thread("fix capture loop"));
        store.AddProject(project);
        store.Save();

        var p = Assert.Single(store.Projects);
        Assert.Equal("active", p.Status);
        Assert.Equal("open", p.Threads[0].Status);
    }
}
