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
- Current Codex lane includes the WinUI shell plus shared-core duplicate intelligence, AI/planning projection work, AI privacy/safety guards, preview/runtime parity work, and safe optimization breadth so scanner truth can feed deterministic planning and review surfaces cleanly
- Current Codex lane is now app-side integration of the recent backend wave plus shell refinement: surface `C-032` source-aware saved plans, `C-033` checkpoint/VSS posture, and `C-034` optimization rollback truth in the existing mission-control UI while keeping the shell stable across 1080p and wider layouts

## What Claude Code Can Own In Parallel
- Backend-heavy implementation packets for service/runtime work that do not touch `src/Atlas.App/**`
- Storage repositories, service hardening, installer/service registration, AI contract enforcement, deeper scanner/file-understanding work, sensitivity intelligence, duplicate evidence/confidence work, tests, eval cases, red-team datasets, installer/recovery research, prompt/risk docs, and markdown-first coordination work
- Current Claude lane is now the next backend wave after the completed `C-040` to `C-043` pass:
  - `C-044` optimization execution history query APIs
  - `C-045` VSS restore request and fallback APIs
  - `C-046` optimization batch apply preview and session summary
  - `C-047` conversation summary filters and drill-in
- Claude should avoid editing `src/Atlas.App/` UI files unless a packet explicitly says otherwise
- Claude should use `.planning/claude/CLAUDE-INBOX.md` and `.planning/claude/CLAUDE-OUTBOX.md` as the primary coordination channel

## Recommended Branch Split
- `codex/ui-ux-shell`
- `codex/claude-structure`

## Required Handoff Updates
Update this file plus `spec/DECISIONS.md`, `spec/TASKS.md`, and the relevant `.planning/` files whenever scope, safety rules, or interface contracts change.
