using System;
using System.Text.Json;
using AccountaBall.Core.Models;
using Xunit;

namespace AccountaBall.Core.Tests;

public class TaskSessionTests
{
    [Fact]
    public void Duration_CalculatesCorrectly()
    {
        var start = DateTimeOffset.UtcNow;
        var end = start.AddSeconds(300);
        var session = new TaskSession("write proposal", start, end);
        Assert.True(Math.Abs(session.Duration - 300) < 0.001);
    }

    [Fact]
    public void JsonRoundtrip_PreservesTaskAndDuration()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1000);
        var end = DateTimeOffset.FromUnixTimeSeconds(1300);
        var s2 = new TaskSession("write tests", start, end);
        var json = JsonSerializer.Serialize(s2);
        var decoded = JsonSerializer.Deserialize<TaskSession>(json)!;
        Assert.Equal("write tests", decoded.Task);
        Assert.True(Math.Abs(decoded.Duration - 300) < 0.001);
    }
}
