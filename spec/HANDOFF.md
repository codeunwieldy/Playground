# Handoff

## Shared Planning Files
- `.planning/PROJECT.md`
- `.planning/REQUIREMENTS.md`
- `.planning/ROADMAP.md`
- `.planning/STATE.md`
- `.planning/codebase/`
- `.planning/claude/`
- `.planning/obsidian/`

## What Codex App Owns
- WinUI project, visual system, motion, interaction design, XAML implementation, shell orchestration, restore/build/run verification, and the final UX shape of plan review, optimization, undo, and command-center flows
- Current Codex UI lane also includes the new `Atlas Memory` workspace, which is session-backed today and intended to consume Claude's `C-008` persisted history APIs next
- Current next Codex lane is scan-intelligence UX: session-pair comparison, drift drill-in, provenance surfaces, hotspot summaries, and a rescan-story lane that now consumes `C-016` while staying ready for Claude's queued `C-017` incremental-composition activation work

## What Claude Code Can Own In Parallel
- Backend-heavy implementation packets for service/runtime work that do not touch `src/Atlas.App/**`
- Storage repositories, service hardening, installer/service registration, AI contract enforcement, tests, eval cases, red-team datasets, installer/recovery research, prompt/risk docs, and markdown-first coordination work
- Claude should avoid editing `src/Atlas.App/` UI files unless a packet explicitly says otherwise
- Claude should use `.planning/claude/CLAUDE-INBOX.md` and `.planning/claude/CLAUDE-OUTBOX.md` as the primary coordination channel

## Recommended Branch Split
- `codex/ui-ux-shell`
- `codex/claude-structure`

## Required Handoff Updates
Update this file plus `spec/DECISIONS.md`, `spec/TASKS.md`, and the relevant `.planning/` files whenever scope, safety rules, or interface contracts change.
