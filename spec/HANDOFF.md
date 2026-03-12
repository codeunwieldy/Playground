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
- Current next Codex lane is retained duplicate cleanup preview integration and shell stabilization: wire `C-027` cleanup-preview truth into the current Memory / Plan Review duplicate drill-in, keep the new Memory route stable after the navigation crash fix, and continue the responsive page refactor across the shell

## What Claude Code Can Own In Parallel
- Backend-heavy implementation packets for service/runtime work that do not touch `src/Atlas.App/**`
- Storage repositories, service hardening, installer/service registration, AI contract enforcement, deeper scanner/file-understanding work, sensitivity intelligence, duplicate evidence/confidence work, tests, eval cases, red-team datasets, installer/recovery research, prompt/risk docs, and markdown-first coordination work
- Current next Claude lane is duplicate cleanup plan preview APIs: expose deterministic, read-only retained-session duplicate cleanup plan preview using the existing cleanup planner and batch preview logic without touching `src/Atlas.App/**`
- Claude should avoid editing `src/Atlas.App/` UI files unless a packet explicitly says otherwise
- Claude should use `.planning/claude/CLAUDE-INBOX.md` and `.planning/claude/CLAUDE-OUTBOX.md` as the primary coordination channel

## Recommended Branch Split
- `codex/ui-ux-shell`
- `codex/claude-structure`

## Required Handoff Updates
Update this file plus `spec/DECISIONS.md`, `spec/TASKS.md`, and the relevant `.planning/` files whenever scope, safety rules, or interface contracts change.
