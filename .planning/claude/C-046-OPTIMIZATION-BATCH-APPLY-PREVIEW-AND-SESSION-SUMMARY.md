# C-046 Optimization Batch Apply Preview and Session Summary Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-042`

## Goal

Extend the single-fix optimization request lane into a bounded multi-fix preview/apply surface so Atlas can later apply a trusted batch of safe fixes from one optimization session.

## Boundaries

- No `src/Atlas.App/**`
- Prefer additive evolution on top of `C-042`
- Keep the batch restricted to curated safe optimization kinds only

## Deliverables

- Add bounded batch preview/apply contracts keyed by finding ids
- Return clear included vs blocked findings with reasons
- Persist execution history and rollback truth for every applied item
- Add tests for mixed eligibility, partial blocking, and success-state reporting

## Notes From Codex

- The app can now drive one lead safe fix.
- The next natural move is a trusted batch control surface, not broader optimizer scope.
