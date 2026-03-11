# C-002: Storage and History Structural Plan

**Subagent B - Storage and History Analyst**
**Date:** 2026-03-11

## Executive Summary

This document defines the repository layer architecture for Atlas File Intelligence. It establishes repository boundaries, table ownership, retention strategies, and search indexing approaches based on the existing schema in `AtlasDatabaseBootstrapper.cs` (lines 24-99).

---

## 1. Repository Boundaries

### 1.1 Repository Classification

The storage layer should be organized into **four distinct repositories**, each with clear ownership and responsibility boundaries.

| Repository | Responsibility | Primary Tables |
|------------|----------------|----------------|
| `IPlanRepository` | Plan lifecycle: creation, retrieval, batch tracking | `plans`, `execution_batches` |
| `IRecoveryRepository` | Rollback/undo state, quarantine lifecycle | `undo_checkpoints`, `quarantine_items` |
| `IConversationRepository` | Chat history, search, AI traces | `conversations`, `conversation_fts`, `prompt_traces` |
| `IConfigurationRepository` | Policy profiles and user settings | `policy_profiles` |

Additionally, a cross-cutting `IOptimizationRepository` handles:
| Repository | Responsibility | Primary Tables |
|------------|----------------|----------------|
| `IOptimizationRepository` | Optimization findings and recommendations | `optimization_findings` |

### 1.2 Interface Contracts (Sketch)

```csharp
// File: src/Atlas.Storage/Repositories/IPlanRepository.cs
namespace Atlas.Storage.Repositories;

public interface IPlanRepository
{
    // Plan CRUD
    Task<string> SavePlanAsync(PlanGraph plan, CancellationToken ct = default);
    Task<PlanGraph?> GetPlanAsync(string planId, CancellationToken ct = default);
    Task<IReadOnlyList<PlanSummary>> ListPlansAsync(int limit = 50, int offset = 0, CancellationToken ct = default);
    Task<bool> DeletePlanAsync(string planId, CancellationToken ct = default);

    // Execution batch management
    Task<string> SaveBatchAsync(ExecutionBatch batch, CancellationToken ct = default);
    Task<ExecutionBatch?> GetBatchAsync(string batchId, CancellationToken ct = default);
    Task<IReadOnlyList<ExecutionBatch>> GetBatchesForPlanAsync(string planId, CancellationToken ct = default);
}

// File: src/Atlas.Storage/Repositories/IRecoveryRepository.cs
namespace Atlas.Storage.Repositories;

public interface IRecoveryRepository
{
    // Checkpoint management
    Task<string> SaveCheckpointAsync(UndoCheckpoint checkpoint, CancellationToken ct = default);
    Task<UndoCheckpoint?> GetCheckpointAsync(string checkpointId, CancellationToken ct = default);
    Task<UndoCheckpoint?> GetCheckpointForBatchAsync(string batchId, CancellationToken ct = default);
    Task<IReadOnlyList<UndoCheckpoint>> ListCheckpointsAsync(int limit = 50, CancellationToken ct = default);

    // Quarantine lifecycle
    Task<string> SaveQuarantineItemAsync(QuarantineItem item, CancellationToken ct = default);
    Task<QuarantineItem?> GetQuarantineItemAsync(string quarantineId, CancellationToken ct = default);
    Task<IReadOnlyList<QuarantineItem>> GetExpiredQuarantineItemsAsync(CancellationToken ct = default);
    Task<bool> DeleteQuarantineItemAsync(string quarantineId, CancellationToken ct = default);
    Task<int> PurgeExpiredQuarantineAsync(CancellationToken ct = default);
}

// File: src/Atlas.Storage/Repositories/IConversationRepository.cs
namespace Atlas.Storage.Repositories;

public interface IConversationRepository
{
    // Conversation history
    Task<string> SaveConversationAsync(Conversation conversation, CancellationToken ct = default);
    Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken ct = default);
    Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(int limit = 100, CancellationToken ct = default);

    // Full-text search (leverages conversation_fts)
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 25, CancellationToken ct = default);
    Task RebuildSearchIndexAsync(CancellationToken ct = default);

    // Prompt trace (for debugging/audit)
    Task<string> SavePromptTraceAsync(PromptTrace trace, CancellationToken ct = default);
    Task<IReadOnlyList<PromptTrace>> GetTracesForStageAsync(string stage, int limit = 50, CancellationToken ct = default);
    Task<int> PurgeOldTracesAsync(TimeSpan maxAge, CancellationToken ct = default);
}

// File: src/Atlas.Storage/Repositories/IConfigurationRepository.cs
namespace Atlas.Storage.Repositories;

public interface IConfigurationRepository
{
    Task<string> SaveProfileAsync(PolicyProfile profile, CancellationToken ct = default);
    Task<PolicyProfile?> GetProfileAsync(string profileName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListProfileNamesAsync(CancellationToken ct = default);
    Task<bool> DeleteProfileAsync(string profileName, CancellationToken ct = default);
}

// File: src/Atlas.Storage/Repositories/IOptimizationRepository.cs
namespace Atlas.Storage.Repositories;

public interface IOptimizationRepository
{
    Task<string> SaveFindingAsync(OptimizationFinding finding, CancellationToken ct = default);
    Task<OptimizationFinding?> GetFindingAsync(string findingId, CancellationToken ct = default);
    Task<IReadOnlyList<OptimizationFinding>> GetFindingsByKindAsync(OptimizationKind kind, CancellationToken ct = default);
    Task<IReadOnlyList<OptimizationFinding>> ListRecentFindingsAsync(int limit = 100, CancellationToken ct = default);
    Task<bool> DismissFindingAsync(string findingId, CancellationToken ct = default);
}
```

### 1.3 Dependency Graph

```
Atlas.Service
    |
    +-- IPlanRepository
    |       +-- uses AtlasJsonCompression (payload serialization)
    |
    +-- IRecoveryRepository
    |       +-- uses AtlasJsonCompression
    |       +-- cross-references IPlanRepository (batch lookup)
    |
    +-- IConversationRepository
    |       +-- uses AtlasJsonCompression
    |       +-- manages FTS5 sync
    |
    +-- IConfigurationRepository
    |       +-- uses AtlasJsonCompression
    |
    +-- IOptimizationRepository
            +-- uses AtlasJsonCompression
```

---

## 2. Table Ownership and Schema Design

### 2.1 Current Schema Reference

Source: `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs:24-99`

| Table | Owner Repository | Compression | Key Column | Foreign Keys |
|-------|------------------|-------------|------------|--------------|
| `plans` | `IPlanRepository` | `payload` (BLOB) | `plan_id` | None |
| `execution_batches` | `IPlanRepository` | `payload` (BLOB) | `batch_id` | `plan_id` (logical) |
| `undo_checkpoints` | `IRecoveryRepository` | `payload` (BLOB) | `checkpoint_id` | `batch_id` (logical) |
| `quarantine_items` | `IRecoveryRepository` | `payload` (BLOB) | `quarantine_id` | `plan_id` (logical) |
| `optimization_findings` | `IOptimizationRepository` | `payload` (BLOB) | `finding_id` | None |
| `conversations` | `IConversationRepository` | `payload` (BLOB) | `conversation_id` | None |
| `conversation_fts` | `IConversationRepository` | N/A (FTS5) | `conversation_id` | Shadows `conversations` |
| `prompt_traces` | `IConversationRepository` | `prompt_payload`, `response_payload` | `trace_id` | None |
| `policy_profiles` | `IConfigurationRepository` | `payload` (BLOB) | `profile_name` | None |

### 2.2 Schema Enhancement Recommendations

#### 2.2.1 Add Indexes for Common Queries

```sql
-- For IPlanRepository: list plans by date
CREATE INDEX IF NOT EXISTS idx_plans_created ON plans(created_utc DESC);

-- For IPlanRepository: find batches by plan
CREATE INDEX IF NOT EXISTS idx_batches_plan ON execution_batches(plan_id);

-- For IRecoveryRepository: find checkpoints by batch
CREATE INDEX IF NOT EXISTS idx_checkpoints_batch ON undo_checkpoints(batch_id);

-- For IRecoveryRepository: find expired quarantine
CREATE INDEX IF NOT EXISTS idx_quarantine_expiry ON quarantine_items(retention_until_utc);

-- For IRecoveryRepository: find quarantine by plan
CREATE INDEX IF NOT EXISTS idx_quarantine_plan ON quarantine_items(plan_id);

-- For IConversationRepository: list by date, find expired
CREATE INDEX IF NOT EXISTS idx_conversations_created ON conversations(created_utc DESC);
CREATE INDEX IF NOT EXISTS idx_conversations_expires ON conversations(expires_utc)
    WHERE expires_utc IS NOT NULL;

-- For IConversationRepository: prompt traces by stage
CREATE INDEX IF NOT EXISTS idx_traces_stage ON prompt_traces(stage, created_utc DESC);

-- For IOptimizationRepository: findings by kind
CREATE INDEX IF NOT EXISTS idx_findings_kind ON optimization_findings(kind, created_utc DESC);
```

#### 2.2.2 Add Status Columns for State Machines

**UNCERTAINTY FLAG:** The current schema lacks explicit status fields. The following additions are recommended but require validation against UI requirements:

```sql
-- Add to plans table (migration required)
ALTER TABLE plans ADD COLUMN status TEXT NOT NULL DEFAULT 'draft';
-- Values: draft | approved | executing | completed | failed | cancelled

-- Add to execution_batches table
ALTER TABLE execution_batches ADD COLUMN status TEXT NOT NULL DEFAULT 'pending';
-- Values: pending | running | completed | rolled_back | failed
```

#### 2.2.3 Add Soft Delete Support

```sql
-- Add to all primary tables for soft-delete support
ALTER TABLE plans ADD COLUMN deleted_utc TEXT NULL;
ALTER TABLE execution_batches ADD COLUMN deleted_utc TEXT NULL;
ALTER TABLE conversations ADD COLUMN deleted_utc TEXT NULL;
```

### 2.3 Payload Compression Strategy

Reference: `src/Atlas.Storage/AtlasJsonCompression.cs:1-27`

The existing Brotli compression (`CompressionLevel.SmallestSize`) is appropriate for:
- Large JSON payloads (plans, batches, conversations)
- Infrequent reads (historical data)

**Recommendation:** Add a compression threshold:

```csharp
// File: src/Atlas.Storage/AtlasJsonCompression.cs (enhancement)
public static class AtlasJsonCompression
{
    private const int CompressionThresholdBytes = 256;

    public static byte[] CompressIfLarge(string payload)
    {
        var raw = Encoding.UTF8.GetBytes(payload);
        if (raw.Length < CompressionThresholdBytes)
        {
            // Prefix with 0x00 to indicate uncompressed
            var result = new byte[raw.Length + 1];
            result[0] = 0x00;
            raw.CopyTo(result, 1);
            return result;
        }

        // Prefix with 0x01 to indicate Brotli compressed
        using var output = new MemoryStream();
        output.WriteByte(0x01);
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(raw, 0, raw.Length);
        }
        return output.ToArray();
    }
}
```

---

## 3. Retention Jobs

### 3.1 Retention Policy Overview

| Data Type | Default Retention | Configurable | Trigger |
|-----------|-------------------|--------------|---------|
| Plans (completed) | 90 days | Yes | Scheduled job |
| Plans (draft/cancelled) | 7 days | Yes | Scheduled job |
| Execution batches | Tied to parent plan | No | Cascade |
| Undo checkpoints | 30 days after batch completion | Yes | Scheduled job |
| Quarantine items | `retention_until_utc` | Yes (`StorageOptions.QuarantineRetentionDays`) | Scheduled job |
| Conversations | `expires_utc` or 180 days | Yes | Scheduled job |
| Prompt traces | 14 days | Yes | Scheduled job |
| Optimization findings | 30 days | Yes | Scheduled job |

Reference: `src/Atlas.Storage/StorageOptions.cs:13` for `QuarantineRetentionDays`

### 3.2 Retention Job Interface

```csharp
// File: src/Atlas.Storage/Retention/IRetentionJob.cs
namespace Atlas.Storage.Retention;

public interface IRetentionJob
{
    string JobName { get; }
    Task<RetentionResult> ExecuteAsync(CancellationToken ct = default);
}

public sealed record RetentionResult(
    int RecordsDeleted,
    long BytesFreed,
    TimeSpan Duration,
    IReadOnlyList<string> Errors);
```

### 3.3 Retention Job Implementations

```csharp
// File: src/Atlas.Storage/Retention/QuarantineRetentionJob.cs
namespace Atlas.Storage.Retention;

public sealed class QuarantineRetentionJob(
    IRecoveryRepository recoveryRepository,
    ILogger<QuarantineRetentionJob> logger) : IRetentionJob
{
    public string JobName => "QuarantineCleanup";

    public async Task<RetentionResult> ExecuteAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var errors = new List<string>();
        long bytesFreed = 0;

        var expired = await recoveryRepository.GetExpiredQuarantineItemsAsync(ct);
        int deleted = 0;

        foreach (var item in expired)
        {
            try
            {
                // Delete physical file first
                if (File.Exists(item.CurrentPath))
                {
                    var info = new FileInfo(item.CurrentPath);
                    bytesFreed += info.Length;
                    File.Delete(item.CurrentPath);
                }

                // Then remove database record
                await recoveryRepository.DeleteQuarantineItemAsync(item.QuarantineId, ct);
                deleted++;
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to purge {item.QuarantineId}: {ex.Message}");
                logger.LogWarning(ex, "Quarantine cleanup failed for {QuarantineId}", item.QuarantineId);
            }
        }

        return new RetentionResult(deleted, bytesFreed, sw.Elapsed, errors);
    }
}

// File: src/Atlas.Storage/Retention/ConversationRetentionJob.cs
namespace Atlas.Storage.Retention;

public sealed class ConversationRetentionJob(
    IConversationRepository conversationRepository,
    IOptions<RetentionOptions> options,
    ILogger<ConversationRetentionJob> logger) : IRetentionJob
{
    public string JobName => "ConversationCleanup";

    public async Task<RetentionResult> ExecuteAsync(CancellationToken ct = default)
    {
        // Delete conversations past expires_utc or older than max age
        var deleted = await conversationRepository.PurgeExpiredAsync(
            options.Value.MaxConversationAgeDays, ct);

        // Rebuild FTS index after large deletions
        if (deleted > 100)
        {
            await conversationRepository.RebuildSearchIndexAsync(ct);
        }

        return new RetentionResult(deleted, 0, TimeSpan.Zero, Array.Empty<string>());
    }
}
```

### 3.4 Retention Options

```csharp
// File: src/Atlas.Storage/Retention/RetentionOptions.cs
namespace Atlas.Storage.Retention;

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public int CompletedPlanRetentionDays { get; set; } = 90;
    public int DraftPlanRetentionDays { get; set; } = 7;
    public int UndoCheckpointRetentionDays { get; set; } = 30;
    public int MaxConversationAgeDays { get; set; } = 180;
    public int PromptTraceRetentionDays { get; set; } = 14;
    public int OptimizationFindingRetentionDays { get; set; } = 30;

    // Scheduling
    public TimeSpan JobInterval { get; set; } = TimeSpan.FromHours(6);
    public TimeOnly PreferredRunTime { get; set; } = new(3, 0); // 3 AM
}
```

### 3.5 Retention Worker

```csharp
// File: src/Atlas.Service/Services/RetentionWorker.cs
namespace Atlas.Service.Services;

public sealed class RetentionWorker(
    IEnumerable<IRetentionJob> jobs,
    IOptions<RetentionOptions> options,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(options.Value.JobInterval, stoppingToken);

            foreach (var job in jobs)
            {
                try
                {
                    var result = await job.ExecuteAsync(stoppingToken);
                    logger.LogInformation(
                        "Retention job {JobName} completed: {Deleted} records, {Bytes} bytes freed",
                        job.JobName, result.RecordsDeleted, result.BytesFreed);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Retention job {JobName} failed", job.JobName);
                }
            }
        }
    }
}
```

---

## 4. Search and Index Strategy

### 4.1 Current FTS Implementation

Reference: `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs:71-72`

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS conversation_fts USING fts5(
    conversation_id,
    summary,
    content
);
```

**Current Limitations:**
- Only indexes `conversations` table
- No support for searching plans, findings, or traces
- No ranking/boosting configuration

### 4.2 Enhanced Search Schema

```sql
-- Unified search across multiple entity types
CREATE VIRTUAL TABLE IF NOT EXISTS atlas_search USING fts5(
    entity_type,      -- 'conversation' | 'plan' | 'finding' | 'quarantine'
    entity_id,
    title,            -- summary/scope/target
    content,          -- full searchable text
    metadata,         -- JSON blob for filtering
    tokenize='porter unicode61'
);

-- Separate triggers to keep FTS in sync
CREATE TRIGGER IF NOT EXISTS conversations_ai AFTER INSERT ON conversations BEGIN
    INSERT INTO atlas_search(entity_type, entity_id, title, content, metadata)
    VALUES ('conversation', NEW.conversation_id, NEW.summary, '', json_object('kind', NEW.kind));
END;

CREATE TRIGGER IF NOT EXISTS conversations_ad AFTER DELETE ON conversations BEGIN
    DELETE FROM atlas_search WHERE entity_type = 'conversation' AND entity_id = OLD.conversation_id;
END;

CREATE TRIGGER IF NOT EXISTS plans_ai AFTER INSERT ON plans BEGIN
    INSERT INTO atlas_search(entity_type, entity_id, title, content, metadata)
    VALUES ('plan', NEW.plan_id, NEW.summary, NEW.scope, '{}');
END;

CREATE TRIGGER IF NOT EXISTS plans_ad AFTER DELETE ON plans BEGIN
    DELETE FROM atlas_search WHERE entity_type = 'plan' AND entity_id = OLD.plan_id;
END;
```

### 4.3 Search Repository Contract

```csharp
// File: src/Atlas.Storage/Search/ISearchRepository.cs
namespace Atlas.Storage.Search;

public interface ISearchRepository
{
    /// <summary>
    /// Full-text search across all indexed entities.
    /// </summary>
    Task<SearchResultSet> SearchAsync(SearchQuery query, CancellationToken ct = default);

    /// <summary>
    /// Index or re-index a specific entity.
    /// </summary>
    Task IndexEntityAsync(string entityType, string entityId, string title, string content, CancellationToken ct = default);

    /// <summary>
    /// Remove an entity from the search index.
    /// </summary>
    Task RemoveEntityAsync(string entityType, string entityId, CancellationToken ct = default);

    /// <summary>
    /// Rebuild the entire search index from source tables.
    /// </summary>
    Task RebuildIndexAsync(IProgress<double>? progress = null, CancellationToken ct = default);
}

public sealed record SearchQuery(
    string QueryText,
    IReadOnlyList<string>? EntityTypes = null,  // null = all types
    int Limit = 25,
    int Offset = 0,
    DateTimeOffset? CreatedAfter = null,
    DateTimeOffset? CreatedBefore = null);

public sealed record SearchResultSet(
    IReadOnlyList<SearchHit> Hits,
    int TotalCount,
    TimeSpan SearchDuration);

public sealed record SearchHit(
    string EntityType,
    string EntityId,
    string Title,
    string Snippet,       // Highlighted excerpt
    double Rank);
```

### 4.4 Search Implementation Notes

**FTS5 Query Building:**

```csharp
// File: src/Atlas.Storage/Search/SearchRepository.cs (sketch)
private string BuildFts5Query(SearchQuery query)
{
    var sb = new StringBuilder();
    sb.Append("SELECT entity_type, entity_id, title, snippet(atlas_search, 3, '<b>', '</b>', '...', 32), rank ");
    sb.Append("FROM atlas_search WHERE atlas_search MATCH @query");

    if (query.EntityTypes?.Count > 0)
    {
        var types = string.Join(",", query.EntityTypes.Select(t => $"'{t}'"));
        sb.Append($" AND entity_type IN ({types})");
    }

    sb.Append(" ORDER BY rank LIMIT @limit OFFSET @offset");
    return sb.ToString();
}
```

**Conversation Content Extraction:**

The `conversations.payload` BLOB contains compressed JSON. For search indexing, content must be extracted:

```csharp
private async Task<string> ExtractSearchableContent(string conversationId, byte[] payload)
{
    var json = AtlasJsonCompression.Decompress(payload);
    var doc = JsonDocument.Parse(json);
    var sb = new StringBuilder();

    if (doc.RootElement.TryGetProperty("messages", out var messages))
    {
        foreach (var msg in messages.EnumerateArray())
        {
            if (msg.TryGetProperty("content", out var content))
            {
                sb.AppendLine(content.GetString());
            }
        }
    }

    return sb.ToString();
}
```

### 4.5 Search Index Maintenance

| Operation | Trigger | Strategy |
|-----------|---------|----------|
| Insert | After entity creation | Sync via SQLite trigger |
| Update | After entity modification | Repository calls `IndexEntityAsync` |
| Delete | After entity deletion | Sync via SQLite trigger |
| Rebuild | Manual or post-upgrade | Background job with progress |

**UNCERTAINTY FLAG:** The exact structure of `conversations.payload` is not defined in the current codebase. The content extraction logic above assumes a `messages` array, but this should be validated against actual data.

---

## 5. Implementation Roadmap

### Phase 1: Repository Foundation
1. Create `src/Atlas.Storage/Repositories/` directory
2. Implement base repository with connection management
3. Implement `IPlanRepository` and `IRecoveryRepository`
4. Add DI registration in service startup

### Phase 2: History and Search
1. Implement `IConversationRepository`
2. Enhance FTS schema with triggers
3. Implement `ISearchRepository`
4. Wire up to existing `AtlasPipeServerWorker` (reference: lines 52-63)

### Phase 3: Retention Jobs
1. Create `src/Atlas.Storage/Retention/` directory
2. Implement retention jobs for each data type
3. Add `RetentionWorker` to service startup
4. Add configuration options to `appsettings.json`

### Phase 4: Testing and Validation
1. Unit tests for each repository
2. Integration tests for retention jobs
3. Load testing for search performance
4. Migration testing for schema changes

---

## 6. Open Questions and Uncertainties

| Item | Uncertainty | Recommendation |
|------|-------------|----------------|
| Conversation payload structure | Not defined in contracts | Define `Conversation` model in `DomainModels.cs` |
| Search ranking weights | No UX requirements | Start with FTS5 defaults, tune based on feedback |
| Cascade delete behavior | Not specified | Soft-delete first, hard-delete via retention |
| Transaction boundaries | Cross-repo operations | Consider Unit of Work pattern for complex ops |
| Database migration strategy | No tooling in place | Consider FluentMigrator or manual SQL scripts |

---

## 7. File References

| File | Relevance |
|------|-----------|
| `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs:24-99` | Current schema definitions |
| `src/Atlas.Storage/AtlasJsonCompression.cs:1-27` | Brotli compression utilities |
| `src/Atlas.Storage/StorageOptions.cs:1-14` | Storage configuration |
| `src/Atlas.Core/Contracts/DomainModels.cs:1-194` | Domain entity definitions |
| `src/Atlas.Service/Services/PlanExecutionService.cs:149-176` | Quarantine creation logic |
| `src/Atlas.Service/Services/AtlasStartupWorker.cs:1-23` | Database initialization |
| `.planning/codebase/ARCHITECTURE.md:1-49` | System architecture overview |
