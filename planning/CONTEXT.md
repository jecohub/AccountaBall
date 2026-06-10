# Planning

Layer 2 context for /planning workspace.

## Purpose
Specs, architecture decisions, and design docs for AccountaBall.

## Current Priorities
1. SwiftUI floating panel skeleton (NSPanel, always-on-top, smiling ball face, hover-to-show)
2. ScreenCaptureKit integration — periodic screenshot loop (~5s)
3. Vision.framework OCR — extract text from screenshots
4. Claude API integration — classify on-task / off-task / done
5. Notification + widget reaction on state change

## Architectural Principles
- Build in layers: UI first, capture second, AI last
- Fake the AI layer during UI development (stub responses)
- Keep the AI provider swappable (Claude API → Ollama) behind a protocol
- All capture and AI work happens off the main thread
- No external dependencies except the Claude/Ollama HTTP call

## Inputs
- Layer 0: ../CLAUDE.md
- Architecture diagram: ../AccountaBall Architecture (SVG)

## Output Locations
- Specs → specs/
- Decision records → decisions/

## Naming
- Specs: feature-name_spec.md
- Decision records: YYYY-MM-DD-decision-title.md
