# Architecture

## Source Of Truth
- Brownfield codebase map: `.planning/codebase/`
- Product roadmap: `.planning/ROADMAP.md`
- Current project memory: `.planning/STATE.md`

## Runtime Shape
- `Atlas.App`: WinUI 3 native shell, plan review, history, settings, command center
- `Atlas.Service`: privileged service for scanning, execution, rollback, optimization, and later checkpoint orchestration
- `Atlas.Core`: shared contracts, pipe envelopes, policy logic, risk and rollback primitives
- `Atlas.AI`: OpenAI Responses integration, prompt packs, and heuristic fallback planner
- `Atlas.Storage`: SQLite bootstrap, compressed artifact helpers, and future repositories

## Trust Boundary
- App talks to service over named pipes using protobuf envelopes
- The model emits proposals only; all local mutation flows through service validation
- `AtlasPolicyEngine` is the hard gate between AI output and machine action
- Rollback metadata is generated before execution and persisted for later recovery

## Current Scaffold Status
- Implemented foundation: contracts, policy engine, rollback planner, storage bootstrapper, service host, scanner/executor skeleton, optimizer scaffold, WinUI shell scaffold
- Missing product-grade behavior: deep file understanding, live repositories, bound UI flows, VSS orchestration, realtime voice, installer completion, large-scale verification
