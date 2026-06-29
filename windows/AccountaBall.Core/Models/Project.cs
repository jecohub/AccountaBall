using System;
using System.Collections.Generic;

namespace AccountaBall.Core.Models;

/// A long-lived workstream. Threads are owned (cascade) children. Part of the
/// Project -> Thread -> Contribution -> Capture context spine.
public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; }
    public List<string> Aliases { get; set; } = new();
    public string Status { get; set; } = "active";     // active | dormant | done
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastTouchedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Summary { get; set; } = "";
    public List<string> People { get; set; } = new();
    public List<string> CodeContext { get; set; } = new();
    public List<string> Refs { get; set; } = new();
    public List<Thread> Threads { get; } = new();

    public Project(string title) => Title = title;
}

/// An open loop under a project.
public sealed class Thread
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; }
    public string Status { get; set; } = "open";       // open | resolved
    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }

    public Thread(string title) => Title = title;
}
