using System.Text.Json;
using AccountaBall.Core.Models;
using Xunit;

namespace AccountaBall.Core.Tests;

public class TaskItemTests
{
    [Fact]
    public void Construction_StoresFields()
    {
        var item = new TaskItem { Task = "write proposal", Context = "for client meeting" };
        Assert.Equal("write proposal", item.Task);
        Assert.Equal("for client meeting", item.Context);
        Assert.False(item.IsComplete);
        Assert.Equal(0, item.TimeOnTask);
    }

    [Fact]
    public void JsonRoundtrip_PreservesFieldsAndId()
    {
        var item = new TaskItem { Task = "write proposal", Context = "for client meeting" };
        var json = JsonSerializer.Serialize(item);
        var decoded = JsonSerializer.Deserialize<TaskItem>(json)!;
        Assert.Equal(item.Task, decoded.Task);
        Assert.Equal(item.Id, decoded.Id);
        Assert.False(decoded.IsComplete);
        Assert.Equal(item.Context, decoded.Context);
        Assert.Equal(item.TimeOnTask, decoded.TimeOnTask);
    }

    [Fact]
    public void Empty_IsBlankAndNotFilledIn()
    {
        var empty = new TaskItem();
        Assert.Equal("", empty.Task);
        Assert.Equal("", empty.Context);
        Assert.False(empty.IsFilledIn);
    }

    [Fact]
    public void IsFilledIn_RequiresBothNonBlank()
    {
        var item = new TaskItem { Task = "write proposal", Context = "for client meeting" };
        Assert.True(item.IsFilledIn);
        var ws = new TaskItem { Task = "  ", Context = "\t" };
        Assert.False(ws.IsFilledIn);
    }
}
