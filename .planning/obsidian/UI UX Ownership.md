---
title: UI UX Ownership
tags:
  - atlas
  - ux
  - ui
  - codex
status: locked
aliases:
  - UI Ownership
---

# UI UX Ownership

> [!important] Locked Ownership
> Codex is the primary owner for all UX/UI and WinUI implementation because the repository-local UI guidance and design skill context live here.

## Codex Owns

- `src/Atlas.App/`
- WinUI navigation and layout
- Motion and interaction patterns
- Theme tokens and visual language
- Plan review canvas design
- Dashboard, undo, optimization, and command-center experience

## Claude Should Avoid

- XAML layout changes
- Visual restyling
- Animation decisions
- Interaction-pattern changes
- UX copy changes that alter the intended UI flow without coordination

## Shared Surface

Claude can support UI work indirectly by contributing:
- backend contracts
- data-shape notes
- test plans
- explainability and audit requirements

Return to [[Atlas Hub]] or [[Claude Collaboration]].
