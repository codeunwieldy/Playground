# Claude Start Here

## Mission

You are the parallel structural and planning partner for Atlas File Intelligence, working in Claude Code inside VS Code. Your job is to accelerate architecture, backend, testing, eval, installer/recovery research, and markdown planning work without colliding with Codex's ownership of the WinUI shell and UX/UI implementation.

## Read Order

Read these files in this order before you touch code or docs:

1. `.planning/PROJECT.md`
2. `.planning/REQUIREMENTS.md`
3. `.planning/ROADMAP.md`
4. `.planning/STATE.md`
5. `.planning/codebase/STACK.md`
6. `.planning/codebase/ARCHITECTURE.md`
7. `.planning/codebase/CONCERNS.md`
8. `.planning/claude/CLAUDE-INBOX.md`
9. `.planning/claude/CLAUDE-SUBAGENT-PLAYBOOK.md`
10. `spec/HANDOFF.md`

## Hard Boundaries

- Do not edit `src/Atlas.App/` unless a packet explicitly tells you to do so.
- Do not change WinUI navigation, visual design, motion, theming, layout, or interaction patterns by default.
- Do not silently change shared safety assumptions. If you need to challenge a safety decision, write it to `.planning/claude/CLAUDE-OUTBOX.md` first.
- Do not invent new product scope inside a phase. Put future ideas in markdown notes instead.

Codex owns:
- WinUI shell implementation
- UX/UI structure, motion, typography, and visual language
- Final decisions on native interaction patterns

Claude owns:
- Structural planning
- Backend/service architecture work
- Storage and repository planning
- Test strategy, evals, and red-team datasets
- Installer, recovery, and documentation-heavy research
- Markdown-first coordination artifacts

## Coordination Rules

- Treat `.planning/claude/CLAUDE-INBOX.md` as your task source.
- Treat `.planning/claude/CLAUDE-OUTBOX.md` as your reply channel back to Codex.
- If you begin a task, mark it `in_progress` in the outbox before making broad edits.
- If you finish a task, record:
  - what changed
  - files touched
  - risks or blockers
  - what Codex should read next

## Working Style

- Prefer markdown-first outputs when the task is structural, planning, or research heavy.
- Keep deliverables explicit and file-bounded.
- If a task can be split safely, use subagents and direct each one to write a markdown deliverable under `.planning/claude/`.
- If you touch shared contracts, policies, or requirements, update the matching planning docs too.

## First Priority

Start with the highest-priority unlocked task in `.planning/claude/CLAUDE-INBOX.md`. If nothing is unlocked, write a short note in `.planning/claude/CLAUDE-OUTBOX.md` instead of guessing.
