# Claude Subagent Playbook

Use subagents when the task is broad enough to split into independent markdown deliverables without causing file conflicts.

## Default Split Pattern

### Subagent A - Safety and Policy
- Read:
  - `src/Atlas.Core/Policies/`
  - `tests/Atlas.Core.Tests/`
- Output:
  - `.planning/claude/SUBAGENT-A-SAFETY.md`
- Focus:
  - policy edge cases
  - missing tests
  - unsafe operations that should be blocked or escalated

### Subagent B - Storage and History
- Read:
  - `src/Atlas.Storage/`
  - `src/Atlas.Service/Services/PlanExecutionService.cs`
- Output:
  - `.planning/claude/SUBAGENT-B-STORAGE.md`
- Focus:
  - repositories
  - retention jobs
  - search/index design
  - checkpoint persistence

### Subagent C - AI Contracts and Evals
- Read:
  - `src/Atlas.AI/`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `evals/`
- Output:
  - `.planning/claude/SUBAGENT-C-AI-EVALS.md`
- Focus:
  - strict schema design
  - prompt packs
  - eval categories
  - red-team inputs

### Subagent D - Deployment and Recovery
- Read:
  - `installer/`
  - `src/Atlas.Service/`
  - `.planning/ROADMAP.md`
- Output:
  - `.planning/claude/SUBAGENT-D-DEPLOYMENT.md`
- Focus:
  - service install path
  - MSI/WiX needs
  - recovery/VSS notes
  - failure modes

## Guardrails For Every Subagent

- Do not touch `src/Atlas.App/`
- Do not redesign UI flows or visuals
- Write findings and plans in markdown first
- Include exact file references for every recommendation
- Flag uncertainty instead of guessing

## Merge Pattern

After subagents finish:

1. Summarize overlaps and disagreements
2. Consolidate into the parent task deliverable
3. Update `.planning/claude/CLAUDE-OUTBOX.md`
4. Only then propose code edits or follow-up packets
