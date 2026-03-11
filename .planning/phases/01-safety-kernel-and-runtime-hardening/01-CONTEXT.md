# Phase 1: Safety Kernel and Runtime Hardening - Context

**Gathered:** 2026-03-11
**Status:** Ready for planning
**Source:** Brownfield repo audit and product brief synthesis

<domain>
## Phase Boundary

This phase locks the Atlas trust boundary and runtime assumptions. It is about tightening what the system is allowed to do, documenting how the native app and service are expected to run, and expanding tests around protected-path, mutable-root, and sync-folder behavior. It does not add advanced file intelligence, rich UI binding, VSS, or voice execution yet.

</domain>

<decisions>
## Implementation Decisions

### Safety boundary
- The model remains proposal-only and may emit structured plan operations only
- The privileged service owns all file mutation, rollback, and optimization execution
- Protected Windows, Program Files, app-critical, and credential-like locations are blocked
- Sync-managed folders remain excluded by default

### Runtime and packaging
- The current dev workflow stays unpackaged and x64-focused for iteration
- The shipping path should continue toward MSI/WiX because Atlas needs multiple executables and service installation
- WinUI 3 and .NET 8 remain the current app/service stack unless Phase 1 finds a hard blocker

### Verification expectations
- Policy logic must be covered by tests before more destructive executor capabilities are added
- Brownfield scaffold gaps must be called out explicitly so later phases do not assume they are already solved

### Claude's Discretion
- How to structure the Phase 1 plans internally
- Whether red-team fixtures live in `tests/`, `evals/`, or both
- Whether runtime-hardening work needs one plan or a separate plan for deployment assumptions

</decisions>

<specifics>
## Specific Ideas

- Audit `src/Atlas.Core/Policies/PolicyProfileFactory.cs`
- Audit `src/Atlas.Core/Policies/AtlasPolicyEngine.cs`
- Audit `src/Atlas.Service/Program.cs` and installer direction under `installer/`
- Expand `tests/Atlas.Core.Tests/PolicyEngineTests.cs` and related safety coverage
- Carry official platform constraints forward: unpackaged/self-contained WinUI deployment guidance, single-project MSIX limitations, USN journal planning, and VSS checkpoint gating

</specifics>

<deferred>
## Deferred Ideas

- Deeper content classification and parser integrations
- Rich plan review UI data binding
- Repository implementations for conversation history and undo timeline
- Voice pipeline implementation
- Full optimization modules and VSS execution work

</deferred>

---

*Phase: 01-safety-kernel-and-runtime-hardening*
*Context gathered: 2026-03-11 via brownfield planning initialization*
