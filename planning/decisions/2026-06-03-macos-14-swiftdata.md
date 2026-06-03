# 2026-06-03 — Raise minimum to macOS 14 (Sonoma) for SwiftData

## Status
Accepted

## Context
v3 introduces persistent, cross-session task memory: per-cycle timelines,
justification events, allowances, and a `KnowledgeTask` corpus with completion
recaps. v1/v2 stored only the declared task list in UserDefaults + JSON, which
does not model these relationships well (cascade deletes, queries by normalized
title, one-to-many completions/allowances).

SwiftData gives us a typed, relational, on-device store with `@Model` types and
`@Relationship(deleteRule:)`, but it requires **macOS 14 (Sonoma)**. v2 targeted
macOS 13 (Ventura) for the ScreenCaptureKit minimum.

## Decision
Adopt SwiftData for all v3 persistence and raise the deployment minimum to
**macOS 14**. The six models live in `Sources/AccountaBall/Models/Persistence.swift`
(`WorkSession`, `TimelineEntry`, `JustificationEvent`, `KnowledgeTask`,
`Allowance`, `TaskCompletion`), built via `AccountaBallStore.makeContainer`.
All `@Model` types and the store are annotated `@available(macOS 14, *)` and the
build target is `arm64-apple-macosx14.0`.

## Consequences
- Drops macOS 13 (Ventura) users. Acceptable for a prototype; Sonoma adoption is
  high and ScreenCaptureKit users are already on recent macOS.
- The SwiftData macro plugin ships with Xcode, not CommandLineTools `swiftc`, so
  the Makefile loads `libSwiftDataMacros.dylib` explicitly via
  `-load-plugin-library`.
- The declared task list stays in UserDefaults (it is ephemeral per session and
  reset on load); only the cross-session knowledge lives in SwiftData.
- SwiftData to-many relationships are **unordered** — callers that need
  chronological order (e.g. coalescing a session timeline into recap steps) must
  sort by timestamp. See `AccountabilityEngine.summarizeCompletion`.

## Alternatives considered
- **Core Data**: more boilerplate, no `@Model` ergonomics; SwiftData wraps it.
- **Hand-rolled JSON files**: poor fit for relational queries and cascade
  deletes; would reimplement what SwiftData gives for free.
