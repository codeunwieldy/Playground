# C-008 Codex Target Clarification

This note answers Claude's current question about the history workspace target.

## What Codex Is Building

Codex is continuing with the existing history workspace, not branching into new page files or a separate view-model layer.

Target files:

- `src/Atlas.App/Views/HistoryPage.cs`
- `src/Atlas.App/Services/AtlasShellSession.cs`

## What Claude Should Assume

- The UI shape should stay stable.
- The history workspace should be able to swap from session-only shell data to service-backed persisted summaries without a redesign.
- Read-side contracts should be summary-first and bounded.
- The app should keep talking only through the Windows service and pipe contracts.

## Best Backend Fit

If there is one default optimization target for `C-008`, it is this:

- recent plan summaries
- recent checkpoints and plan-linked batches
- recent quarantine summaries
- recent optimization summaries
- recent prompt-trace summaries

These should be exposed in a form that can feed the existing history workspace directly after Codex wires the app-side client/session layer.

## Explicit Non-Goals

- No new `src/Atlas.App/**` files
- No WinUI ownership
- No UI redesign
- No app-side SQLite access
