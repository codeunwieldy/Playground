# C-008 Persisted History and Query APIs Packet

## Owner

Claude Code in VS Code

## Priority

Highest

## Why This Is Open Now

`C-007` tightened the AI boundary and added prompt-trace persistence. That means Atlas now has meaningful stored artifacts across several domains:

- plans
- execution batches
- undo checkpoints
- quarantine items
- optimization findings
- prompt traces

But the app still cannot query that stored history through the service. The WinUI shell is currently forced to behave as if history is session-only, even though the storage layer now exists.

This packet should expose persisted history back to the app through safe read-only service APIs so Codex can build real history/timeline surfaces on top of them.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, shell navigation, or visual design
- Do not weaken the existing service-only mutation boundary
- These APIs are read-only; do not introduce app-side direct database access
- Keep contracts additive and version-friendly

## Read First

1. `src/Atlas.Core/Contracts/PipeContracts.cs`
2. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
3. `src/Atlas.App/Services/AtlasPipeClient.cs`
4. `src/Atlas.Storage/Repositories/IPlanRepository.cs`
5. `src/Atlas.Storage/Repositories/IRecoveryRepository.cs`
6. `src/Atlas.Storage/Repositories/IOptimizationRepository.cs`
7. `src/Atlas.Storage/Repositories/IConversationRepository.cs`
8. `src/Atlas.Storage/Repositories/*.cs`
9. `.planning/claude/C-005-PERSISTENCE-INTEGRATION.md`
10. `.planning/claude/C-006-EXECUTION-HARDENING.md`
11. `.planning/claude/C-007-STRICT-AI-PIPELINE.md`

## Goal

Expose persisted Atlas history through the Windows service so the app can query:

- recent plans
- batches for a plan
- recent undo checkpoints
- quarantine items
- recent optimization findings
- prompt traces

without the app ever touching SQLite directly.

## Required Deliverables

### 1. Pipe contracts for read-side history

Add additive request/response contracts in `PipeContracts.cs` for history queries.

Keep them compact and app-friendly.

Good candidates:

- recent history snapshot request/response
- recent plans list request/response
- plan detail request/response
- recent checkpoints list request/response
- recent quarantine items list request/response
- recent optimization findings list request/response
- recent prompt traces list request/response

You do not need to expose every repository method if a smaller set gives the app a strong first history surface.

### 2. Service handlers

Add read-only history routes to `AtlasPipeServerWorker`.

Desired behavior:

- query repositories only
- no mutation side effects
- bounded result sizes
- graceful empty responses
- clear error handling for missing entities

Prefer a small number of high-value routes over many narrow ones if that keeps the API easier to evolve.

### 3. History DTO shaping

Do not send giant compressed payload blobs to the app unless detail is explicitly requested.

Provide summary/detail shaping that matches UI needs:

- summary rows for timelines
- optional detail calls for selected items
- useful timestamps
- enough identifiers to support drill-in later

If you add summary DTOs, keep them deterministic and serializable with protobuf.

### 4. Repository gaps

Fill any missing repository read methods only if required by the new service routes.

Keep this scoped:

- do not redesign repository interfaces casually
- do not rewrite working persistence logic
- if a needed method is missing, add the smallest useful abstraction and document it

### 5. Tests

Add focused tests for the history query layer.

A new test file in an existing backend test project is fine.

Good targets:

- route returns recent plans in descending time order
- route returns checkpoints and quarantine items correctly
- route returns prompt-trace summaries
- empty database returns clean empty responses
- detail request returns the right entity or a clear missing response

Use temporary SQLite databases and avoid live service hosting where possible.

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- exact task status
- files read
- files changed
- new message types/contracts added
- what history can now be queried
- tests added and whether they pass
- any intentionally deferred detail endpoints

## Suggested Subagent Split

If you use Claude subagents, this split should work well:

### Subagent A: Contracts and DTOs

- own new protobuf request/response types
- own summary/detail DTO shaping

### Subagent B: Service handlers and repository read path

- own `AtlasPipeServerWorker` routes
- own minimal repository gap fills

### Subagent C: Tests and verification

- own read-side integration tests
- own empty-state and ordering tests
- own final build/test pass

## Success Criteria

This packet is successful when:

- the app can query persisted history only through the service
- stored plans/checkpoints/quarantine/traces are available through bounded read APIs
- contracts are additive and read-only
- tests exist and pass
- no UI files were touched

## Notes From Codex

- The shell now has a dedicated `Atlas Memory` workspace. Please shape the read-side contracts so the UI can graduate from session-only memory to persisted service-backed history without a redesign.
- Strong first DTO fields would be `id`, `kind`, `timestamp`, `title`, `detail`, and optional `status`, `risk_posture`, `related_plan_id`, or `related_batch_id`.
- If one compact "recent history snapshot" route plus a few detail/list routes covers the need, prefer that over a large set of narrow endpoints.

- I’m moving the shell toward a dedicated history/memory workspace now. What I need from Claude is the read-side service contract that lets that UI stop being session-only.
- Please optimize for app-ready summaries first, not raw storage dumps.
- After this packet, the next likely Claude lane will be inventory graph persistence and delta scanning unless a smaller support packet is needed to round out history detail queries.
