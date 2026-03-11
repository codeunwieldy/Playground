# C-007 Strict AI Pipeline Packet

## Owner

Claude Code in VS Code

## Priority

Highest

## Why This Is Open Now

`C-006` hardened service execution and brought the mutation path much closer to production discipline:

- execution now has preflight validation
- ordering is deterministic
- partial failures return accurate rollback data
- quarantine metadata is much safer
- service-side execution tests now exist

That shifts the biggest backend risk to the AI boundary itself.

Right now the planning client is still too permissive:

- the Responses API schema allows `plan` to be any object
- there is no dedicated semantic validator for AI-authored plans after parse
- voice parsing is still loosely handled
- prompt traces are not being persisted
- `OpenAIOptions` is not being used as the primary runtime configuration source
- there is no focused AI test project covering parsing, failure handling, or trace capture

This packet should make Atlas much stricter and more auditable at the AI edge without touching WinUI or UI code.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, shell interaction, or visual design
- Do not weaken the deterministic local fallback path
- Do not route any filesystem mutation around the service boundary
- Prefer additive changes over broad contract churn
- If a required contract change would materially affect UI bindings or app-side models, stop and report it first

## Read First

1. `src/Atlas.AI/AtlasPlanningClient.cs`
2. `src/Atlas.AI/OpenAIOptions.cs`
3. `src/Atlas.AI/PromptCatalog.cs`
4. `src/Atlas.Core/Contracts/DomainModels.cs`
5. `src/Atlas.Core/Contracts/PipeContracts.cs`
6. `src/Atlas.Core/Policies/AtlasPolicyEngine.cs`
7. `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
8. `src/Atlas.Storage/Repositories/IConversationRepository.cs`
9. `src/Atlas.Service/Program.cs`
10. `.planning/claude/C-003-AI-CONTRACTS.md`
11. `evals/README.md`
12. `evals/fixtures/*.json`

## Goal

Make the Atlas AI pipeline:

- strict about what the model is allowed to return
- validated after parse instead of trusting deserialization alone
- auditable through stored prompt traces
- configurable through `OpenAIOptions`
- covered by focused automated tests

## Required Deliverables

### 1. Strict structured-output definitions

Replace the current loose inline schema in `AtlasPlanningClient.cs` with strongly constrained plan and voice schemas.

Minimum expectations:

- `PlanResponse` schema mirrors the real `PlanGraph`, `PlanOperation`, and `RiskEnvelope` shape closely
- enum-like fields are constrained to valid Atlas values
- confidence and score values are bounded to `0.0-1.0`
- required fields are explicit
- `additionalProperties` is disabled where practical
- voice parsing also uses a defined shape instead of free-form text assumptions

Implementation shape is your call:

- embedded JSON schema resources
- strongly typed schema builders
- or another maintainable approach

But do not leave the `plan` field as an untyped object.

### 2. Post-parse semantic validation

Add a deterministic semantic validation layer after JSON parsing.

This should be separate from pure schema validation and should check things like:

- operation/path requirements by operation kind
- disallowed or obviously protected paths
- confidence/risk consistency
- review escalation when sensitivity or sync risk is elevated
- duplicate-delete requirements for `DeleteToQuarantine`
- rollback requirements when structural or destructive operations exist

If validation fails:

- fail closed
- persist a trace if trace persistence is implemented in this packet
- return the deterministic fallback plan rather than trusting the invalid model output

### 3. Prompt-trace persistence

Use the existing `prompt_traces` table and `IConversationRepository` contract to persist AI traces.

Primary scope:

- planning traces
- voice-intent traces

Desired captured fields:

- stage
- prompt payload
- raw response payload
- timestamp

If you need a small additive contract improvement for prompt traces, keep it surgical and document it.

### 4. Conversation repository completion

Implement `ConversationRepository` under `src/Atlas.Storage/Repositories/` if it is still missing.

Minimum priority is prompt-trace support. If the full conversation and FTS path falls out cleanly, implement it now too.

If full FTS search would blow scope:

- still implement prompt-trace storage/retrieval/listing properly
- document what remains for conversation search
- do not fake or stub repository behavior silently

### 5. Runtime wiring

Wire the strict AI pipeline into the real app/service runtime.

Expected outcomes:

- `OpenAIResponsesPlanningClient` uses `OpenAIOptions` as the primary source for base URL, API key, model, and prompt sizing
- environment-variable overrides are acceptable if kept explicit and documented
- planning requests store traces
- voice-intent requests store traces
- invalid model output falls back deterministically and leaves an audit trail when possible

If a cleaner seam is needed, introduce one. Prefer small composable pieces over broad rewrites.

### 6. Tests

Add focused AI-layer tests.

A new `tests/Atlas.AI.Tests/` project is fine if that keeps things clean.

Good targets:

- strict schema accepts valid planner output
- extra or malformed fields fail validation
- invalid enum values or out-of-range confidence values fail closed
- semantic validation rejects protected-path or unsafe-delete outputs
- fallback path triggers on invalid model output
- `OpenAIOptions` values are actually used
- prompt traces persist for planning and voice requests

Use deterministic fake HTTP responses. Do not depend on live network calls.

### 7. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- exact task status
- files read
- files changed
- what strictness was added
- what invalid outputs now fail closed
- tests added and whether they pass
- any intentionally deferred parts

## Suggested Subagent Split

If you use Claude subagents, this split should work well:

### Subagent A: Schema and semantic validation

- own strict response schemas
- own parse/validation pipeline
- own fallback-on-invalid behavior

### Subagent B: Trace persistence and repository completion

- own `ConversationRepository`
- own prompt-trace persistence
- own runtime wiring for planning and voice flows

### Subagent C: Tests and verification

- own fake HTTP response harnesses
- own AI-layer tests
- own final build/test pass

## Success Criteria

This packet is successful when:

- the model can no longer return an effectively untyped plan object
- invalid AI output fails closed and falls back deterministically
- prompt traces are stored for planning and voice flows
- `OpenAIOptions` actually governs runtime behavior
- focused AI tests exist and pass
- no UI files were touched

## Notes From Codex

- The shell now has a more explicit command center, plan review canvas, and recovery framing. That UI is ready to explain the planner, but I need the planner boundary itself to be much stricter before we rely on live AI more heavily.
- Keep the trust boundary clean: the model proposes, validators judge, and the service remains the only component that can mutate files.
- After this packet, the next likely Claude lane will be either inventory persistence/delta scanning or persisted read APIs for timeline/history surfaces.
