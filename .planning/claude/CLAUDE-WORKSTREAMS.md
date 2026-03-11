# Claude Workstreams

## Current Role

These workstreams are the default areas Claude should own while Codex handles UI/UX and WinUI implementation.

## Active Workstreams

### CL-01 Safety Kernel and Test Expansion
- Objective: Strengthen policy, path-safety, sync-folder, and destructive-action rules
- Primary files:
  - `src/Atlas.Core/Policies/AtlasPolicyEngine.cs`
  - `src/Atlas.Core/Policies/PathSafetyClassifier.cs`
  - `src/Atlas.Core/Policies/PolicyProfileFactory.cs`
  - `tests/Atlas.Core.Tests/`
- Deliverables:
  - markdown audit of missing edge cases
  - proposed test matrix
  - later code changes only when the task is explicitly opened

### CL-02 Storage and Repository Architecture
- Objective: Turn schema bootstrap into a real repository and retention system
- Primary files:
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
  - `src/Atlas.Storage/AtlasJsonCompression.cs`
  - `src/Atlas.Service/Services/PlanExecutionService.cs`
  - `.planning/codebase/ARCHITECTURE.md`
- Deliverables:
  - repository plan
  - table usage plan
  - retention and search design notes

### CL-03 AI Planning Contracts and Evals
- Objective: Tighten plan schemas, prompt packs, trace capture, and red-team coverage
- Primary files:
  - `src/Atlas.AI/AtlasPlanningClient.cs`
  - `src/Atlas.AI/PromptCatalog.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `evals/`
- Deliverables:
  - markdown plan for strict structured outputs
  - eval suite outline
  - unsafe-request test cases

### CL-04 Deployment, VSS, and Recovery Research
- Objective: Prepare the app/service/install/recovery path without crossing into UI work
- Primary files:
  - `installer/Bundle.wxs`
  - `installer/Product.wxs`
  - `src/Atlas.Service/`
  - `.planning/ROADMAP.md`
- Deliverables:
  - deployment checklist
  - service install/recovery notes
  - VSS eligibility and failure-mode notes

### CL-05 Audit and Collaboration Operations
- Objective: Keep planning, handoff, and review artifacts healthy as parallel work grows
- Primary files:
  - `.planning/`
  - `spec/HANDOFF.md`
  - `spec/DECISIONS.md`
  - `spec/TASKS.md`
- Deliverables:
  - collaboration hygiene notes
  - doc-drift warnings
  - backlog cleanup suggestions

## Priority Order

1. CL-01 Safety Kernel and Test Expansion
2. CL-02 Storage and Repository Architecture
3. CL-03 AI Planning Contracts and Evals
4. CL-04 Deployment, VSS, and Recovery Research
5. CL-05 Audit and Collaboration Operations
