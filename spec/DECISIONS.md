# Locked Decisions

- Planning source of truth is now `.planning/PROJECT.md`, `.planning/REQUIREMENTS.md`, `.planning/ROADMAP.md`, and `.planning/STATE.md`
- UI stack remains WinUI 3 with an unpackaged, x64-focused development workflow
- Service runtime remains a .NET 8 worker service
- IPC remains named pipes with protobuf envelopes
- Planner contract stays limited to a strict operation DSL
- Deletion stays quarantine-first with no direct model-driven purge path
- Rollback stays inverse-operations first, with VSS reserved for later high-impact checkpoint scenarios
- Sync folders remain excluded by default
- Optimization stays limited to curated low-risk categories for auto-fix
- AI mode remains cloud-first with local fallback and local risk re-validation
- Codex and Claude Code should split work using `.planning/` docs, `.planning/claude/` packets, and `spec/HANDOFF.md`
- Codex retains ownership of WinUI, UX, animation, and visual-system implementation
