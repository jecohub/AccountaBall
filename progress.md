# AccountaBall — Progress Notes
_Last updated: 2026-06-03 (v3 code-complete; manual QA pending)_

---

## Status: v1 ✅ · v2 ✅ · v3 code-complete (manual QA pending)

- **v1** (single-task floating ball): fully shipped, runs.
- **v2** (multi-task, animations, progress tracking): all 14 tasks committed,
  102 tests passing at handoff. Pending human visual QA.
- **v3** (local task-memory: SwiftData, allowances, recaps, cross-session
  reuse, Ollama default): **all 19 tasks implemented and committed.**
  `make test` → **185/185 passing** (deterministic across repeated runs).
  `make build` → clean. The only remaining work is **manual QA** (needs a
  running app with Ollama/OpenRouter + screen-recording permission — see the
  checklist below) and a **security follow-up** (rotate the leaked v2 key).

---

## What changed this session (the async-test blocker is resolved)

The previous blocker — the v3 test runner hung at 99% CPU and never finished —
is **fixed**. Root cause: the runner blocked the main thread (via
`DispatchGroup.wait()` / nested `RunLoop.main.run(mode:before:)` / semaphores)
while the `@MainActor` async test work needed that same thread, so it could
never be scheduled. Fix: drive the whole run from a single top-level
`Task { @MainActor in await runAllTests() }` + `RunLoop.main.run()`, awaiting
the engine's async methods **directly** (no manual pumping); `reportAndExit()`
calls `exit()` to break the run loop. (`Tests/TestRunner/main.swift`,
`Tests/MicroTest.swift`.)

While getting the suite green, a **real product bug** was found and fixed:
`summarizeCompletion` fed `session.entries` (a SwiftData to-many relationship,
which is **unordered**) straight into the timeline coalescer, so recap steps
could render in random order. Now sorted by `TimelineEntry.at` first.

---

## v3 task status (1–19)

| # | Task | Status | Commit (src repo) |
|---|------|--------|-------------------|
| 1 | label on `MultiTaskResult` | ✅ | `29a749f` |
| 2 | `ExcuseVerdict` | ✅ | `9298248` |
| 3 | `TimelineCoalescer` | ✅ | `4526dc2` |
| 4 | `TaskMatcher` | ✅ | `d0177c6` |
| 5 | `DurationDelta` | ✅ | `1f4a6a2` |
| 6 | SwiftData models | ✅ | `13ef7e9` |
| 7 | `KnowledgeTaskSnapshot` | ✅ | `038457b` |
| 8 | AIService protocol extended | ✅ | `58c86a9` |
| 9 | OpenRouter prompts/parsers | ✅ | `82898bb` |
| 10 | `OllamaAIService` | ✅ | `c1b33fd` |
| 11 | provider factory; remove key | ✅ | `9c4ddf3` |
| 12 | engine + `WorkSession` | ✅ | `00358da` + `b71a837` |
| 13 | engine + allowances + justifications | ✅ | `edf5885` |
| 14 | engine + completion recap | ✅ | `edf5885` (+ `f0c9c5a` tests) |
| 15 | engine + task matching | ✅ | `1fff851` (models) + `edf5885` |
| 16 | debug log + rotation | ✅ | `3a46d94` + `f0c9c5a` (tests) |
| 17 | UI: setup match-confirm + steps hint | ✅ built | `83a17ba` |
| 18 | UI: allowance confirm-on-reuse + recap | ✅ built | `cf45c86` |
| 19 | docs + decision records | ✅ | outer repo (this commit) |

Commits `1fff851`, `3a46d94`, `edf5885`, `f0c9c5a` landed the formerly-stuck
Tasks 13–16 (split into buildable, dependency-ordered commits). `83a17ba` and
`cf45c86` are the Task 17/18 UI. Tasks 17–18 are **build-verified only** — they
have no unit tests by design; correctness needs the manual QA below.

---

## ⚠️ Security follow-up (still open, from Task 11)

The v2 source had a real OpenRouter API key hardcoded in `AppDelegate.swift`.
It was removed from the working tree at `9c4ddf3`, **but it remains in git
history** (every commit before `9c4ddf3`) and in
`planning/plans/2026-06-02-accountaball-v2-implementation.md`.
**Action: rotate the key at openrouter.ai** (revoke the leaked one, issue a new
one). v3 defaults to local Ollama, so a new key is only used when
`AI_PROVIDER=openrouter` is set explicitly.

---

## Manual QA checklist (the remaining work — needs you)

Run `cd src && AI_PROVIDER=ollama make run` (Ollama running with a pulled
model), then repeat with `AI_PROVIDER=openrouter OPENROUTER_API_KEY=… make run`.

- [ ] Per-cycle entries appear in the log; timeline persists across relaunch
      (SwiftData store in Application Support).
- [ ] Off-task → excuse → **justified** creates an allowance; the same activity
      no longer flags and time credits to the task.
- [ ] Off-task → excuse → **not justified** is still logged but creates no
      allowance.
- [ ] Completing a task shows the recap: total time + summary + steps.
- [ ] Repeating a previously-finished task: setup shows the match prompt; on
      Yes, the allowance confirm-on-reuse prompt fires once on the ball;
      completion shows the faster/slower comparison line.
- [ ] Missing-provider hint shows when `AI_PROVIDER=openrouter` without a key,
      or when Ollama isn't running.
- [ ] All v2 visual flows still work (welcome bounce, setup table, edge ball,
      progress, 3-point completion).

---

## How to Run / Test

```bash
cd /Users/jericodelacruz/Desktop/AccountaBall/src
make test     # custom micro-test runner (NOT XCTest/Swift Testing) — 185 tests
make build    # compile app only
make run      # build + launch floating ball (needs Ollama or OPENROUTER_API_KEY)
```

Stop the app: `pkill -f AccountaBallApp`.

> Note: the test runner uses `RunLoop.main.run()` and never returns until the
> tests call `exit()`. If you ever add new suites, keep them inside the single
> top-level `Task { @MainActor in await runAllTests() }` — do **not** add
> semaphores or `DispatchGroup.wait()` on the main thread (that was the deadlock).
