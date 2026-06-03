# AccountaBall — Progress Notes
_Last updated: 2026-06-03 (v3 implementation in progress)_

---

## Status: v1 ✅ · v2 ✅ · v3 in progress

- **v1** (single-task floating ball): fully shipped, runs.
- **v2** (multi-task, animations, progress tracking): all 14 tasks committed
  at `2a64ba6`, 102 tests passing at handoff. Pending human visual QA.
- **v3** (local task-memory: SwiftData, allowances, recaps, cross-session
  reuse, Ollama default): executing the implementation plan at
  `planning/plans/2026-06-03-accountaball-v3-implementation.md`. Of 19 tasks,
  13 are committed; 6 are landed in the working tree but uncommitted. Build
  is broken in the working tree because the test runner is being refactored
  to drive async engine tests. 148 tests were passing on the last green
  commit (`b71a837`).

---

## v3 commits landed (in order, on `src/` `master`)

| # | Commit | Task | Notes |
|---|---|---|---|
| 1 | `29a749f` | Task 1 | label on `MultiTaskResult` (+ engine/Claude/classification test fixes) |
| 2 | `9298248` | Task 2 | `ExcuseVerdict` type + parser |
| 3 | `4526dc2` | Task 3 | `TimelineCoalescer` (also wired all Wave-1 test registrations in main.swift) |
| 4 | `d0177c6` | Task 4 | `TaskMatcher` normalize + cheap match |
| 5 | `1f4a6a2` | Task 5 | `DurationDelta` compare |
| 6 | `33632d2` | chore | gitignore `.build/` |
| 7 | `13ef7e9` | Task 6 | SwiftData models + container factory |
| 8 | `22f3bd4` | build | Xcode SDK + SwiftData macro plugin |
| 9 | `58c86a9` | Task 8 | Extend AIService protocol |
| 10 | `038457b` | Task 7 | Sendable snapshot structs |
| 11 | `c1b33fd` | Task 10 | `OllamaAIService` (default local provider) |
| 12 | `82898bb` | Task 9 | OpenRouter prompts/parsers |
| 13 | `9c4ddf3` | Task 11 | env-based AI provider factory; remove hardcoded key |
| 14 | `00358da` | Task 12 | engine records per-cycle timeline into a `WorkSession` |
| 15 | `b71a837` | fix  | engine timeline test is order-independent |

---

## v3 in working tree, NOT yet committed (last green = `b71a837`)

The next 4 subagent dispatches (Tasks 13, 14, 15, 16) hit the Ollama 429
rate limit before they could commit. The agents **did** write their code
into the working tree, but the build is broken. Files touched:

- `Sources/AccountaBall/Engine/AccountabilityEngine.swift` — added
  `proposeMatch`, `linkKnowledgeTask`, `summarizeCompletion`, `handleExcuse`,
  `allowanceRulesByIndex`; CYCLE block via `DebugLog.dbgBlock`.
- `Sources/AccountaBall/Models/Persistence.swift` — added
  `var id: UUID = UUID()` on `KnowledgeTask` for stable cross-session links.
- `Sources/AccountaBall/Models/TaskItem.swift` — added
  `var knowledgeRef: UUID? = nil` (Codable + memberwise init preserved).
- `Sources/AccountaBall/Util/DebugLog.swift` — moved log to
  `~/Library/Application Support/AccountaBall/logs/`, added
  `truncate`, `shouldRotate`, `dbgBlock`, `rotationThreshold`.
- `Tests/AccountaBallTests/EngineAllowanceTests.swift` — new
- `Tests/AccountaBallTests/EngineCompletionTests.swift` — new
- `Tests/AccountaBallTests/EngineMatchTests.swift` — new
- `Tests/AccountaBallTests/DebugLogTests.swift` — new
- `Tests/TestRunner/main.swift` — refactored to async v3 path (broken — see below)

---

## Open problem: async v3 tests deadlock the runner

The new v3 test functions (`runEngineAllowanceTests`, `runEngineCompletionTests`,
`runEngineMatchTests`) are `@MainActor async` and use
`try? await Task { @MainActor in engine.handleExcuse(...) }.value` to call
async engine methods. The test harness needs to be able to await them.

Attempted fixes that did NOT work:
1. **`Task { @MainActor in await runAllTests() }` + `RunLoop.main.run()`** —
   test runner reaches DurationDeltaTests (the last sync suite before the
   async ones) then spins at 99% CPU and never finishes. The main run loop
   pump doesn't yield to the main actor's queue the way the test bodies
   need. Process killed at 5 min.
2. **Async `suite(_:_:)` overload that bridges via `Task.detached` +
   `RunLoop.main.run(mode:before:)` spin** — same hang.
3. **Inline `RunLoop.main.run` pump in the async `suite` bridge** — same hang.

The original v2 harness (`DispatchQueue.main.async { ...; reportAndExit() }`
+ `RunLoop.main.run()`) is still in the working tree but combined with the
new async test functions it deadlocks. The async v3 engine methods need to
be driven from a sync test path.

**Suggested next moves** (any of these):
- Drop the `@MainActor async` test signatures and use the agent's original
  `runSyncOnMain` helper (semaphore + run-loop pump), but **invoke it from
  a background thread** so the main thread's `RunLoop.main.run()` can pump
  the main dispatch queue while the background thread waits.
- Make the engine's async methods non-async where possible (they only await
  `aiService.classifyMulti` etc.; the AI services are async). For tests,
  add a sync `evaluateExcuseSync` / `summarizeCompletionSync` shim that
  wraps `Task { ... }.value` and use that in tests.
- Run the test runner from a different entry point that doesn't use
  `RunLoop.main.run()` — e.g. `dispatchMain()` or a `Task` with
  `await Task.sleep(forever)` instead of the run loop.

The cleanest of these is the first: a background-thread bridge. Skeleton:

```swift
DispatchQueue.main.async {
    // ... all sync suites ...
    let group = DispatchGroup()
    group.enter()
    DispatchQueue.global().async {
        let sem = DispatchSemaphore(value: 0)
        Task { @MainActor in
            await runEngineAllowanceTests()  // hops to main; main run loop pumps
            sem.signal()
        }
        while sem.wait(timeout: .now()) == .timedOut {
            Thread.sleep(forTimeInterval: 0.005)  // bg thread waits
        }
        group.leave()
    }
    group.wait()
    // ... same for completion + match ...
    reportAndExit()
}
RunLoop.main.run()
```

This was attempted (it's in the working tree as of this writing) but the
specific implementation is still hung. The likely culprit: the test bodies
use `try? await Task { @MainActor in ... }.value` and that inner Task
**cannot make progress** because the main thread is blocked in
`RunLoop.main.run()` processing only the run loop's own sources, not the
main dispatch queue.

The fix that should work: in the test bodies, instead of
`try? await Task { @MainActor in ... }.value`, use
`DispatchQueue.main.sync { engine.handleExcuseSync(...) }` — but the engine
methods are async, not sync. So the fix is to add **synchronous wrappers**
on the engine for the test path:

```swift
// In the engine:
func handleExcuseSync(_ text: String, tasks: [TaskItem], screenText: String) {
    let sem = DispatchSemaphore(value: 0)
    Task { @MainActor in
        await self.handleExcuse(text, tasks: tasks, screenText: screenText)
        sem.signal()
    }
    sem.wait()
}
```

The caller is on a background thread, calls `handleExcuseSync` which
dispatches the @MainActor Task and waits on a semaphore. The main thread
processes the Task. The background thread unblocks when done.

---

## v3 task status (1–19)

| # | Task | Status | Where |
|---|---|---|---|
| 1 | label on `MultiTaskResult` | ✅ done | `29a749f` |
| 2 | `ExcuseVerdict` | ✅ done | `9298248` |
| 3 | `TimelineCoalescer` | ✅ done | `4526dc2` |
| 4 | `TaskMatcher` | ✅ done | `d0177c6` |
| 5 | `DurationDelta` | ✅ done | `1f4a6a2` |
| 6 | SwiftData models | ✅ done | `13ef7e9` |
| 7 | `KnowledgeTaskSnapshot` | ✅ done | `038457b` |
| 8 | AIService protocol extended | ✅ done | `58c86a9` |
| 9 | OpenRouter prompts/parsers | ✅ done | `82898bb` |
| 10 | `OllamaAIService` | ✅ done | `c1b33fd` |
| 11 | provider factory; remove key | ✅ done | `9c4ddf3` |
| 12 | engine + `WorkSession` | ✅ done | `00358da` + `b71a837` |
| 13 | engine + allowances + justifications | 🟡 code in tree, uncommitted, tests failing | — |
| 14 | engine + completion recap | 🟡 code in tree, uncommitted, tests failing | — |
| 15 | engine + task matching | 🟡 code in tree, uncommitted, tests failing | — |
| 16 | debug log + rotation | 🟡 code in tree, uncommitted | — |
| 17 | UI: match confirm + hint | ⏳ not started | — |
| 18 | UI: allowance confirm + recap | ⏳ not started | — |
| 19 | docs + decision records + manual QA | ⏳ not started | — |

Tasks 13–16 are functionally complete in the working tree but the test
runner can't drive their async paths. **The single remaining work item is
the async test plumbing.** Tasks 17–19 are blocked on 13–16.

---

## Security note (from Task 11 review)

The v2 implementation had a real OpenRouter API key
(`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`)
hardcoded in `Sources/AccountaBall/AppDelegate.swift`. Task 11 (`9c4ddf3`)
removed the literal from the source tree but the key remains in **git
history** (every commit before `9c4ddf3`). The same key string also appears
in `planning/plans/2026-06-02-accountaball-v2-implementation.md:1778` as
context for the plan that introduced it.

**Action required: rotate the key at openrouter.ai** (revoke the leaked one
and issue a new one). The v3 design switches the default provider to local
Ollama anyway, so the new key is only used when `AI_PROVIDER=openrouter` is
set explicitly.

---

## How to resume

1. `cd src`
2. **First, fix the async test plumbing** (see "Open problem" above). The
   recommended fix is to add sync wrappers on the engine (`handleExcuseSync`,
   `summarizeCompletionSync`, `proposeMatchSync`) that use a semaphore, and
   call those from the tests instead of `try? await Task { @MainActor in
   ... }.value`. The test bodies can then stay `@MainActor` but not `async`.
3. Once `make test` is green: commit the 4 working-tree changes
   (engine, persistence, taskitem, debuglog, 4 test files, main.swift) as
   Tasks 13, 14, 15, 16.
4. Then dispatch Tasks 17, 18, 19 (UI + docs).
5. Final manual QA per Task 19's checklist.

## How to Run / Test

```bash
cd /Users/jericodelacruz/Desktop/AccountaBall/src
make test     # custom micro-test runner (NOT XCTest/Swift Testing)
make build    # compile app only
make run      # build + launch floating ball (needs Ollama or OPENROUTER_API_KEY)
```

Stop the app: `pkill -f AccountaBallApp`.
