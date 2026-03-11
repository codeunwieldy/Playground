# Concerns

## Safety and Product Risk

- `src/Atlas.Service/Services/FileScanner.cs` still classifies mostly by extension, path text, and simple hashing
- `src/Atlas.AI/AtlasPlanningClient.cs` uses a permissive JSON wrapper that should become stricter before real execution power grows
- `src/Atlas.Service/Services/PlanExecutionService.cs` executes move/delete flows without persisted transaction state or robust partial-failure recovery

## Architecture Debt

- The app UI is mostly a premium-looking placeholder and not connected to live service data
- Storage has schema creation but no repositories, migrations, retention workers, or search services
- The named-pipe service boundary exists conceptually, but cross-project workflow depth is still limited

## Windows-Specific Gaps

- No VSS orchestration yet for high-impact recovery
- No USN journal integration yet for scalable rescans
- No completed installer/service-registration flow yet
- No confirmed launch-and-runtime verification flow captured in tests or docs

## UX Gaps

- The plan review surface is not yet a real diff canvas
- Undo history, conversation history, and policy studio are not yet bound to persisted state
- Voice is only represented in the design brief, not in the WinUI shell

## Operational Gaps

- The repo has no commits yet for the current scaffold
- There is no CI pipeline, packaging automation, or release checklist
- `evals/` is present but not populated with usable fixtures

## Recommendation

Treat the current codebase as a strong foundation, not as a nearly finished product. Future work should prioritize safety hardening, persistence, and explainable UX before adding broader autonomous behavior.
