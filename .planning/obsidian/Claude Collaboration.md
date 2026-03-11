---
title: Claude Collaboration
tags:
  - atlas
  - claude
  - collaboration
status: active
---

# Claude Collaboration

## Claude Responsibilities

- Structural planning
- Backend and service architecture notes
- Storage and repository planning
- Test strategy and eval planning
- Installer, recovery, and VSS research
- Markdown-first packet work

## Codex Responsibilities

- WinUI shell implementation
- Visual system and motion
- Interaction design
- UX quality control
- Final UI-facing product decisions

> [!warning] UI Boundary
> Claude should not edit `src/Atlas.App/` unless a packet explicitly opens that scope.

## Coordination Files

- [Claude Start Here](../claude/CLAUDE-START-HERE.md)
- [Claude Inbox](../claude/CLAUDE-INBOX.md)
- [Claude Outbox](../claude/CLAUDE-OUTBOX.md)
- [Subagent Playbook](../claude/CLAUDE-SUBAGENT-PLAYBOOK.md)

## Current workflow

1. Codex updates the shared planning docs.
2. Claude reads the packet files and claims an inbox task.
3. Claude uses subagents for independent markdown deliverables when helpful.
4. Claude reports back through the outbox.
5. Codex folds accepted decisions into shared planning and continues UI implementation.

See also [[Obsidian Workflow]] and [[UI UX Ownership]].
