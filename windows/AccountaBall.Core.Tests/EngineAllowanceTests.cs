using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccountaBall.Core.Engine;
using AccountaBall.Core.Models;
using AccountaBall.Core.Services;
using AccountaBall.Core.Storage;
using Xunit;

namespace AccountaBall.Core.Tests;

public class EngineAllowanceTests
{
    [Fact]
    public async Task JustifiedExcuse_CreatesAllowanceAndLinksKnowledgeTask()
    {
        var ai = new VerdictFakeAI(new ExcuseVerdict(true, 0, "watching React tutorials"));
        var (state, engine, store) = EngineTest.MakeWithSession(ai, "write proposal");
        engine.ProcessResult(new MultiTaskResult.OffTask("youtube"));
        engine.ProcessResult(new MultiTaskResult.OffTask("youtube"));

        Assert.Empty(store.KnowledgeTasks);

        var verdict = await engine.HandleExcuseAsync(
            "I'm watching React tutorials on YouTube", state.ActiveTasks, "some screen text");

        Assert.True(verdict.Justified);
        Assert.Single(engine.CurrentSession!.Justifications);
        var ev = engine.CurrentSession.Justifications[0];
        Assert.True(ev.Justified);
        Assert.Equal(0, ev.InferredTaskIndex);
        Assert.Equal("watching React tutorials", ev.Rule);
        Assert.Single(store.KnowledgeTasks);
        var kt = store.KnowledgeTasks[0];
        Assert.Single(kt.Allowances);
        Assert.Equal("watching React tutorials", kt.Allowances[0].Rule);
        Assert.False(kt.Allowances[0].NeedsConfirmation);
        Assert.Contains("write proposal", kt.OriginalTitles);
    }

    [Fact]
    public async Task NotJustifiedExcuse_RecordsEventNoAllowance()
    {
        var ai = new VerdictFakeAI(new ExcuseVerdict(false, null, ""));
        var (state, engine, store) = EngineTest.MakeWithSession(ai, "write proposal");
        engine.ProcessResult(new MultiTaskResult.OffTask("twitter"));
        engine.ProcessResult(new MultiTaskResult.OffTask("twitter"));

        await engine.HandleExcuseAsync("just browsing", state.ActiveTasks, "some screen text");

        Assert.Single(engine.CurrentSession!.Justifications);
        Assert.False(engine.CurrentSession.Justifications[0].Justified);
        Assert.Empty(store.KnowledgeTasks);
    }

    [Fact]
    public async Task ReusingExistingKnowledgeTask_AppendsAllowance()
    {
        var store = new InMemoryStore();
        var existing = new KnowledgeTask("write proposal", DateTimeOffset.UtcNow);
        existing.OriginalTitles = new List<string> { "write proposal" };
        existing.Allowances.Add(new Allowance("reading docs", DateTimeOffset.UtcNow));
        store.AddKnowledgeTask(existing);

        var ai = new VerdictFakeAI(new ExcuseVerdict(true, 0, "watching tutorials"));
        var state = new AppState { Tasks = { new TaskItem { Task = "write proposal", Context = "" } } };
        state.StartSession();
        var engine = new AccountabilityEngine(state, ai, new NullNotifier()) { Store = store };
        engine.BeginSession(state.Tasks);

        await engine.HandleExcuseAsync("watching tutorials", state.ActiveTasks, "screen");

        Assert.Single(store.KnowledgeTasks);   // reused, not duplicated
        var kt = store.KnowledgeTasks[0];
        Assert.Equal(2, kt.Allowances.Count);
        Assert.Equal(new HashSet<string> { "reading docs", "watching tutorials" },
                     kt.Allowances.Select(a => a.Rule).ToHashSet());
    }

    [Fact]
    public void AllowanceLookup_ActiveRulesForMatchingTasksOnly()
    {
        var store = new InMemoryStore();
        var now = DateTimeOffset.UtcNow;
        var kt0 = new KnowledgeTask("write proposal", now);
        kt0.Allowances.Add(new Allowance("react tutorials", now));
        kt0.Allowances.Add(new Allowance("pending one", now, needsConfirmation: true));
        var kt1 = new KnowledgeTask("review slides", now);   // no allowances
        store.AddKnowledgeTask(kt0);
        store.AddKnowledgeTask(kt1);

        var state = new AppState
        {
            Tasks =
            {
                new TaskItem { Task = "write proposal", Context = "" },
                new TaskItem { Task = "review slides", Context = "" },
                new TaskItem { Task = "unrelated task", Context = "" },
            },
        };
        var engine = new AccountabilityEngine(state, new VerdictFakeAI(new ExcuseVerdict(false, null, "")), new NullNotifier())
        {
            Store = store,
        };

        var rules = engine.AllowanceRulesByIndex(state.Tasks);
        Assert.Single(rules);
        Assert.Equal(new[] { "react tutorials" }, rules[0]);   // pending filtered out
        Assert.False(rules.ContainsKey(1));                    // no active allowances
        Assert.False(rules.ContainsKey(2));                    // no knowledge task
    }

    [Fact]
    public async Task HandleExcuse_NoContext_IsNoOp()
    {
        var ai = new VerdictFakeAI(new ExcuseVerdict(true, 0, "rule"));
        var state = new AppState { Tasks = { new TaskItem { Task = "write proposal", Context = "" } } };
        state.StartSession();
        var engine = new AccountabilityEngine(state, ai, new NullNotifier());   // no Store

        var verdict = await engine.HandleExcuseAsync("anything", state.ActiveTasks, "screen");
        Assert.True(verdict.Justified);   // returns the verdict; just persists nothing
    }
}
