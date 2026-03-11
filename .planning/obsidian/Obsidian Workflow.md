---
title: Obsidian Workflow
tags:
  - atlas
  - obsidian
  - workflow
status: active
---

# Obsidian Workflow

## Purpose

Use the Obsidian notes in `.planning/obsidian/` as the human-readable front door for the planning system, while `.planning/` remains the execution-grade source of truth.

## Efficient Usage Plan

> [!tip] Keep It Layered
> Put durable plans in `.planning/`. Use Obsidian notes to navigate, summarize, and cross-link them with less friction.

1. Open [[Atlas Hub]] first.
2. Use [[Claude Collaboration]] to remember the current split of responsibilities.
3. Use [[UI UX Ownership]] before assigning UI-adjacent tasks.
4. Check the live packet files in `../claude/` before launching Claude.
5. Update these notes only when the collaboration model or navigation flow changes.

## Note Design Rules

- Use wikilinks for notes inside `.planning/obsidian/`
- Use normal markdown links for source docs outside the Obsidian note folder
- Prefer short summaries, callouts, and jump links over duplicating whole planning files
- Keep shared decisions synced back to `.planning/PROJECT.md`, `.planning/STATE.md`, or `spec/HANDOFF.md`

## Suggested Tag Pattern

- `#atlas`
- `#atlas/planning`
- `#atlas/claude`
- `#atlas/ui`
- `#atlas/workflow`

## Shared Rhythm

- Codex updates planning and UI direction
- Claude reads packet files and writes results back
- Obsidian notes stay lightweight and navigational

Return to [[Atlas Hub]].
