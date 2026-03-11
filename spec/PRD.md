# Atlas File Intelligence PRD

## Product Goal
Atlas is a Windows 11-first native desktop product for safe AI-guided file organization, duplicate cleanup, rollback-aware recovery, and guarded PC optimization.

## Source Of Truth
- Strategic product framing now lives in `.planning/PROJECT.md`
- Checkable product scope now lives in `.planning/REQUIREMENTS.md`
- Delivery sequencing now lives in `.planning/ROADMAP.md`

## Core Outcomes
- Reorganize user-created and downloaded files into understandable category trees
- Identify safe duplicates conservatively and quarantine them instead of purging them
- Preserve batch-level undo and file-level restore for destructive actions
- Explain why files were classified, protected, moved, or flagged for cleanup
- Support typed and push-to-talk requests through the same guarded command pipeline
- Surface safe PC optimization opportunities without acting like unsafe optimizerware

## Hard Constraints
- The model may propose plans, but never receives direct filesystem mutation capability
- Protected system paths and app-critical paths are always blocked
- Sync-managed folders are excluded by default
- Every destructive action must have an undo or restore path
- The first release is consumer/prosumer, not enterprise fleet management
