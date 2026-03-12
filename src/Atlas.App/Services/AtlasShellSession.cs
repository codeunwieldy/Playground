using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Atlas.Core.Contracts;
using Atlas.Core.Policies;

namespace Atlas.App.Services;

public sealed class AtlasShellSession : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<OptimizationKind> SafeOptimizationKinds =
    [
        OptimizationKind.TemporaryFiles,
        OptimizationKind.CacheCleanup,
        OptimizationKind.DuplicateArchives,
        OptimizationKind.UserStartupEntry
    ];

    private static readonly Dictionary<string, string> CategoryByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".doc"] = "Documents",
        [".docx"] = "Documents",
        [".pdf"] = "Documents",
        [".txt"] = "Documents",
        [".md"] = "Notes",
        [".rtf"] = "Documents",
        [".xls"] = "Spreadsheets",
        [".xlsx"] = "Spreadsheets",
        [".csv"] = "Spreadsheets",
        [".ppt"] = "Presentations",
        [".pptx"] = "Presentations",
        [".jpg"] = "Images",
        [".jpeg"] = "Images",
        [".png"] = "Images",
        [".gif"] = "Images",
        [".webp"] = "Images",
        [".heic"] = "Images",
        [".mp4"] = "Video",
        [".mov"] = "Video",
        [".mkv"] = "Video",
        [".mp3"] = "Audio",
        [".wav"] = "Audio",
        [".zip"] = "Archives",
        [".7z"] = "Archives",
        [".rar"] = "Archives",
        [".exe"] = "Applications",
        [".msi"] = "Installers"
    };

    private readonly AtlasPipeClient pipeClient = new();
    private readonly PathSafetyClassifier pathSafetyClassifier = new();
    private readonly SemaphoreSlim operationLock = new(1, 1);

    private PolicyProfile profile = PolicyProfileFactory.CreateDefault();
    private ScanResponse currentScan = new();
    private PlanResponse currentPlan = new();
    private OptimizationResponse currentOptimization = new();
    private UndoCheckpoint latestCheckpoint = new();
    private HistorySnapshotResponse persistedHistory = new();
    private InventorySnapshotResponse persistedInventorySnapshot = new();
    private InventorySessionListResponse persistedInventorySessions = new();
    private InventorySessionDetailResponse persistedInventoryDetail = new();
    private InventoryFileListResponse persistedInventoryFiles = new();
    private DriftSnapshotResponse persistedDriftSnapshot = new();
    private SessionDiffResponse persistedSessionDiff = new();
    private SessionDiffFilesResponse persistedDiffFiles = new();
    private string connectionModeLabel = "Preview mode";
    private string connectionModeDetail = "Atlas is ready to inspect locally while the privileged service comes online.";
    private string currentFocus = "Run a scan to establish a fresh inventory before Atlas drafts structural changes.";
    private string inventorySummary = "Inventory has not been scanned in this session yet.";
    private string planSummary = "No plan has been drafted yet.";
    private string riskSummary = "No risk envelope is available yet.";
    private string optimizationSummary = "Optimization findings have not been analyzed yet.";
    private string undoSummary = "No execution batches have been staged yet.";
    private string statusLine = "Atlas is standing by.";
    private string planDiffNarrativeText = "Atlas will surface a before-and-after structure narrative once a plan exists.";
    private string commandInterpretationText = "Describe what you want Atlas to do and it will turn that into a guarded next step.";
    private string commandInterpretationDetailText = "Organization, optimization, undo, and policy requests all stay inside one constrained command lane.";
    private string commandOriginLabel = "Typed draft";
    private string commandWorkspaceLabel = "Command Deck";
    private string commandReviewLabel = "Preview before execute";
    private string commandReviewDetailText = "Atlas keeps destructive steps behind plan review, quarantine, and service-side validation.";
    private string commandNextStepLabel = "Run a fresh scan";
    private string commandNextStepDetailText = "Refresh the inventory or type a request to route into planning, optimization, or recovery.";
    private string recentActivitySummaryText = "Atlas has not done anything in this session yet.";
    private string planSignalSummaryText = "Plan posture will appear once Atlas drafts a reviewed plan.";
    private string undoSignalSummaryText = "Recovery posture will appear once Atlas stages an undo story.";
    private string quarantineSummaryText = "Quarantine items will surface here when Atlas routes deletions through reversible retention.";
    private string inventoryMemorySummaryText = "Atlas will surface scan-session memory here as the inventory story grows.";
    private string scanContinuitySummaryText = "Scan continuity will appear here once Atlas can compare persisted sessions.";
    private string rescanStorySummaryText = "Rescan story will appear here once Atlas can compare stored sessions.";
    private string driftReviewSummaryText = "Drift review will appear here once Atlas can compare stored scan sessions more explicitly.";
    private string scanPairSummaryText = "Stored session-pair intelligence will appear here once Atlas can compare two persisted scans.";
    private string driftFileSampleSummaryText = "A bounded changed-path sample will appear here once Atlas can compare stored scan pairs.";
    private string driftHotspotSummaryText = "Drift hotspots will appear here once Atlas can summarize changed-file patterns.";
    private string scanProvenanceSummaryText = "Scan provenance will appear here once Atlas has enough live or stored scan evidence to explain session origin.";
    private string persistedMemorySummaryText = "Service-backed history will appear here when Atlas can refresh stored memory.";
    private string persistedScanSummaryText = "Stored scan sessions will appear here once the service can answer inventory history queries.";
    private string persistedVolumeSummaryText = "Stored volume posture will appear here once Atlas can inspect a persisted scan session.";
    private string persistedFileSampleSummaryText = "Stored file samples will appear here once Atlas can inspect persisted inventory rows.";
    private string persistedPlanSummaryText = "Stored plan drafts will appear here once the service-backed history lane is available.";
    private string persistedFindingSummaryText = "Stored optimization findings will appear here once Atlas refreshes service memory.";
    private string persistedTraceSummaryText = "Stored planning and voice traces will appear here once the service-backed history lane is online.";
    private bool isBusy;
    private bool isLiveMode;
    private bool isInitialized;
    private int quarantineRetentionDays = 30;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PolicyProfile PolicyProfile => profile;

    public ObservableCollection<string> CommandSuggestions { get; } = new();

    public ObservableCollection<AtlasMetricCard> DashboardMetrics { get; } = new();

    public ObservableCollection<AtlasCategoryCard> TopCategories { get; } = new();

    public ObservableCollection<AtlasVolumeCard> Volumes { get; } = new();

    public ObservableCollection<AtlasPlanOperationCard> PlanOperations { get; } = new();

    public ObservableCollection<AtlasOptimizationCard> OptimizationFindings { get; } = new();

    public ObservableCollection<AtlasUndoBatchCard> UndoBatches { get; } = new();

    public ObservableCollection<AtlasQuarantineCard> QuarantineEntries { get; } = new();

    public ObservableCollection<AtlasStructureGroupCard> CurrentStructureGroups { get; } = new();

    public ObservableCollection<AtlasStructureGroupCard> ProposedStructureGroups { get; } = new();

    public ObservableCollection<AtlasMetricCard> PlanMetrics { get; } = new();

    public ObservableCollection<AtlasMetricCard> OptimizationMetrics { get; } = new();

    public ObservableCollection<AtlasMetricCard> UndoMetrics { get; } = new();

    public ObservableCollection<AtlasMetricCard> HistoryMetrics { get; } = new();

    public ObservableCollection<AtlasSignalCard> InventorySignals { get; } = new();

    public ObservableCollection<AtlasStoredMemoryCard> InventoryMemoryCards { get; } = new();

    public ObservableCollection<AtlasSignalCard> ScanContinuitySignals { get; } = new();

    public ObservableCollection<AtlasStoredMemoryCard> RescanStoryCards { get; } = new();

    public ObservableCollection<AtlasStoredMemoryCard> DriftReviewCards { get; } = new();

    public ObservableCollection<AtlasSignalCard> ScanPairSignals { get; } = new();

    public ObservableCollection<AtlasStoredMemoryCard> DriftFileSampleCards { get; } = new();

    public ObservableCollection<AtlasStoredMemoryCard> DriftHotspotCards { get; } = new();

    public ObservableCollection<AtlasSignalCard> ScanProvenanceSignals { get; } = new();

    public ObservableCollection<AtlasSignalCard> PlanSignals { get; } = new();

    public ObservableCollection<string> PlanBlockedReasons { get; } = new();

    public ObservableCollection<AtlasSignalCard> UndoSignals { get; } = new();

    public ObservableCollection<string> MutableRoots { get; } = new();

    public ObservableCollection<string> ProtectedPaths { get; } = new();

    public ObservableCollection<AtlasActivityEntry> ActivityFeed { get; } = new();

    public ObservableCollection<AtlasStoredMemoryCard> PersistedPlanMemory { get; } = new();

    public ObservableCollection<AtlasStoredMemoryCard> PersistedFindingMemory { get; } = new();

    public ObservableCollection<AtlasStoredMemoryCard> PersistedTraceMemory { get; } = new();

    public ObservableCollection<AtlasStoredMemoryCard> PersistedScanSessionMemory { get; } = new();

    public ObservableCollection<AtlasStoredMemoryCard> PersistedVolumeMemory { get; } = new();

    public ObservableCollection<AtlasStoredMemoryCard> PersistedFileSampleMemory { get; } = new();

    public string ConnectionModeLabel
    {
        get => connectionModeLabel;
        private set => SetProperty(ref connectionModeLabel, value);
    }

    public string ConnectionModeDetail
    {
        get => connectionModeDetail;
        private set => SetProperty(ref connectionModeDetail, value);
    }

    public string CurrentFocus
    {
        get => currentFocus;
        private set => SetProperty(ref currentFocus, value);
    }

    public string InventorySummary
    {
        get => inventorySummary;
        private set => SetProperty(ref inventorySummary, value);
    }

    public string PlanSummary
    {
        get => planSummary;
        private set => SetProperty(ref planSummary, value);
    }

    public string RiskSummary
    {
        get => riskSummary;
        private set => SetProperty(ref riskSummary, value);
    }

    public string OptimizationSummary
    {
        get => optimizationSummary;
        private set => SetProperty(ref optimizationSummary, value);
    }

    public string UndoSummary
    {
        get => undoSummary;
        private set => SetProperty(ref undoSummary, value);
    }

    public string StatusLine
    {
        get => statusLine;
        private set => SetProperty(ref statusLine, value);
    }

    public string PlanRationaleText => string.IsNullOrWhiteSpace(currentPlan.Plan.Rationale)
        ? "Atlas will explain the reasoning behind the current plan here."
        : currentPlan.Plan.Rationale;

    public string PlanBenefitText => string.IsNullOrWhiteSpace(currentPlan.Plan.EstimatedBenefit)
        ? "Expected impact will appear after a plan is drafted."
        : currentPlan.Plan.EstimatedBenefit;

    public string PlanRollbackText => string.IsNullOrWhiteSpace(currentPlan.Plan.RollbackStrategy)
        ? "Rollback strategy has not been staged yet."
        : currentPlan.Plan.RollbackStrategy;

    public string PlanDiffNarrativeText
    {
        get => planDiffNarrativeText;
        private set => SetProperty(ref planDiffNarrativeText, value);
    }

    public string CommandInterpretationText
    {
        get => commandInterpretationText;
        private set => SetProperty(ref commandInterpretationText, value);
    }

    public string CommandInterpretationDetailText
    {
        get => commandInterpretationDetailText;
        private set => SetProperty(ref commandInterpretationDetailText, value);
    }

    public string CommandOriginLabel
    {
        get => commandOriginLabel;
        private set => SetProperty(ref commandOriginLabel, value);
    }

    public string CommandWorkspaceLabel
    {
        get => commandWorkspaceLabel;
        private set => SetProperty(ref commandWorkspaceLabel, value);
    }

    public string CommandReviewLabel
    {
        get => commandReviewLabel;
        private set => SetProperty(ref commandReviewLabel, value);
    }

    public string CommandReviewDetailText
    {
        get => commandReviewDetailText;
        private set => SetProperty(ref commandReviewDetailText, value);
    }

    public string CommandNextStepLabel
    {
        get => commandNextStepLabel;
        private set => SetProperty(ref commandNextStepLabel, value);
    }

    public string CommandNextStepDetailText
    {
        get => commandNextStepDetailText;
        private set => SetProperty(ref commandNextStepDetailText, value);
    }

    public string RecentActivitySummaryText
    {
        get => recentActivitySummaryText;
        private set => SetProperty(ref recentActivitySummaryText, value);
    }

    public string PlanSignalSummaryText
    {
        get => planSignalSummaryText;
        private set => SetProperty(ref planSignalSummaryText, value);
    }

    public string UndoSignalSummaryText
    {
        get => undoSignalSummaryText;
        private set => SetProperty(ref undoSignalSummaryText, value);
    }

    public string QuarantineSummaryText
    {
        get => quarantineSummaryText;
        private set => SetProperty(ref quarantineSummaryText, value);
    }

    public string InventoryMemorySummaryText
    {
        get => inventoryMemorySummaryText;
        private set => SetProperty(ref inventoryMemorySummaryText, value);
    }

    public string ScanContinuitySummaryText
    {
        get => scanContinuitySummaryText;
        private set => SetProperty(ref scanContinuitySummaryText, value);
    }

    public string RescanStorySummaryText
    {
        get => rescanStorySummaryText;
        private set => SetProperty(ref rescanStorySummaryText, value);
    }

    public string DriftReviewSummaryText
    {
        get => driftReviewSummaryText;
        private set => SetProperty(ref driftReviewSummaryText, value);
    }

    public string ScanPairSummaryText
    {
        get => scanPairSummaryText;
        private set => SetProperty(ref scanPairSummaryText, value);
    }

    public string DriftFileSampleSummaryText
    {
        get => driftFileSampleSummaryText;
        private set => SetProperty(ref driftFileSampleSummaryText, value);
    }

    public string DriftHotspotSummaryText
    {
        get => driftHotspotSummaryText;
        private set => SetProperty(ref driftHotspotSummaryText, value);
    }

    public string ScanProvenanceSummaryText
    {
        get => scanProvenanceSummaryText;
        private set => SetProperty(ref scanProvenanceSummaryText, value);
    }

    public string PersistedMemorySummaryText
    {
        get => persistedMemorySummaryText;
        private set => SetProperty(ref persistedMemorySummaryText, value);
    }

    public string PersistedScanSummaryText
    {
        get => persistedScanSummaryText;
        private set => SetProperty(ref persistedScanSummaryText, value);
    }

    public string PersistedVolumeSummaryText
    {
        get => persistedVolumeSummaryText;
        private set => SetProperty(ref persistedVolumeSummaryText, value);
    }

    public string PersistedFileSampleSummaryText
    {
        get => persistedFileSampleSummaryText;
        private set => SetProperty(ref persistedFileSampleSummaryText, value);
    }

    public string PersistedPlanSummaryText
    {
        get => persistedPlanSummaryText;
        private set => SetProperty(ref persistedPlanSummaryText, value);
    }

    public string PersistedFindingSummaryText
    {
        get => persistedFindingSummaryText;
        private set => SetProperty(ref persistedFindingSummaryText, value);
    }

    public string PersistedTraceSummaryText
    {
        get => persistedTraceSummaryText;
        private set => SetProperty(ref persistedTraceSummaryText, value);
    }

    public string UndoNotesText => latestCheckpoint.Notes.Count == 0
        ? "No checkpoint notes are available yet."
        : string.Join(" ", latestCheckpoint.Notes);

    public string MutableRootsSummaryText => MutableRoots.Count == 0
        ? "No mutable roots are configured yet."
        : BuildCollectionSummary(MutableRoots, "root");

    public string ProtectedPathsSummaryText => ProtectedPaths.Count == 0
        ? "No protected paths are configured yet."
        : BuildCollectionSummary(ProtectedPaths, "protected path");

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanDraftPlan));
                OnPropertyChanged(nameof(CanPreviewExecution));
                OnPropertyChanged(nameof(CanExecutePlan));
                OnPropertyChanged(nameof(CanPreviewUndo));
                OnPropertyChanged(nameof(CanExecuteUndo));
                OnPropertyChanged(nameof(BusyStateLabel));
            }
        }
    }

    public bool IsLiveMode
    {
        get => isLiveMode;
        private set
        {
            if (SetProperty(ref isLiveMode, value))
            {
                OnPropertyChanged(nameof(CanExecutePlan));
                OnPropertyChanged(nameof(CanExecuteUndo));
            }
        }
    }

    public bool HasPlan => currentPlan.Plan.Operations.Count > 0;

    public bool HasOptimizationFindings => currentOptimization.Findings.Count > 0;

    public bool HasUndoCheckpoint => latestCheckpoint.InverseOperations.Count > 0 || latestCheckpoint.QuarantineItems.Count > 0;

    public bool CanDraftPlan => !IsBusy && currentScan.Inventory.Count > 0;

    public bool CanPreviewExecution => !IsBusy && HasPlan;

    public bool CanExecutePlan => !IsBusy && HasPlan && IsLiveMode;

    public bool CanPreviewUndo => !IsBusy && HasUndoCheckpoint;

    public bool CanExecuteUndo => !IsBusy && HasUndoCheckpoint && IsLiveMode;

    public string BusyStateLabel => IsBusy ? "Atlas is working through the latest request." : StatusLine;

    public int FilesScanned => currentScan.Inventory.Count;

    public int DuplicateCount => currentScan.Duplicates.Count;

    public int OptimizationCount => currentOptimization.Findings.Count;

    public bool ExcludeSyncFoldersByDefault
    {
        get => profile.ExcludeSyncFoldersByDefault;
        set
        {
            if (profile.ExcludeSyncFoldersByDefault == value)
            {
                return;
            }

            profile.ExcludeSyncFoldersByDefault = value;
            OnPropertyChanged();
            RefreshInventorySignals();
        }
    }

    public bool UploadSensitiveContent
    {
        get => profile.UploadSensitiveContent;
        set
        {
            if (profile.UploadSensitiveContent == value)
            {
                return;
            }

            profile.UploadSensitiveContent = value;
            OnPropertyChanged();
        }
    }

    public bool AutoFixLowRiskOptimizations
    {
        get => profile.AllowedAutomaticOptimizationKinds.Count > 0;
        set
        {
            var currentValue = profile.AllowedAutomaticOptimizationKinds.Count > 0;
            if (currentValue == value)
            {
                return;
            }

            profile.AllowedAutomaticOptimizationKinds = value ? SafeOptimizationKinds.ToList() : [];
            OnPropertyChanged();
        }
    }

    public double DuplicateConfidenceThreshold
    {
        get => profile.DuplicateAutoDeleteConfidenceThreshold;
        set
        {
            var clamped = Math.Clamp(value, 0.95d, 1d);
            if (Math.Abs(profile.DuplicateAutoDeleteConfidenceThreshold - clamped) < 0.0001d)
            {
                return;
            }

            profile.DuplicateAutoDeleteConfidenceThreshold = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DuplicateConfidenceThresholdLabel));
        }
    }

    public int QuarantineRetentionDays
    {
        get => quarantineRetentionDays;
        set
        {
            var clamped = Math.Clamp(value, 7, 90);
            if (SetProperty(ref quarantineRetentionDays, clamped))
            {
                OnPropertyChanged(nameof(QuarantineRetentionLabel));
            }
        }
    }

    public string QuarantineRetentionLabel => $"{QuarantineRetentionDays} days";

    public string DuplicateConfidenceThresholdLabel => $"{DuplicateConfidenceThreshold:P1}";

    public async Task InitializeAsync()
    {
        if (isInitialized)
        {
            return;
        }

        isInitialized = true;
        ReplaceAll(CommandSuggestions, BuildCommandSuggestions());
        SyncProfileCollections();
        await RunScanAsync();
        await RefreshHistoryAsync();
    }

    public async Task RefreshHistoryAsync()
    {
        await ExecuteGuardedAsync(
            "Refreshing Atlas memory from the service...",
            async cancellationToken =>
            {
                await RefreshPersistedHistoryAsync(cancellationToken);

                StatusLine = IsLiveMode
                    ? "Stored Atlas history refreshed from the service."
                    : "Atlas could not reach the service, so memory remains session-first.";

                AddActivity(IsLiveMode ? "Memory refreshed" : "Memory refresh unavailable", StatusLine);
            });
    }

    public async Task RunScanAsync()
    {
        await ExecuteGuardedAsync(
            "Scanning the allowed workspace roots...",
            async cancellationToken =>
            {
                var liveResponse = await TryPipeCallAsync<ScanRequest, ScanResponse>(
                    "scan/request",
                    new ScanRequest
                    {
                        FullScan = true,
                        Roots = profile.MutableRoots.ToList(),
                        MaxFiles = 3000
                    },
                    cancellationToken);

                currentScan = liveResponse ?? await Task.Run(() => BuildPreviewScan(cancellationToken), cancellationToken);
                ApplyScanState();

                if (liveResponse is not null)
                {
                    await RefreshPersistedHistoryAsync(cancellationToken);
                }

                CurrentFocus = currentScan.Inventory.Count == 0
                    ? "Atlas did not find mutable user files in the current roots."
                    : $"Atlas is tracking {currentScan.Inventory.Count:N0} user files across {TopCategories.Count} visible categories.";

                InventorySummary = liveResponse is null
                    ? $"Preview scan loaded {currentScan.Inventory.Count:N0} files from the mutable roots. Execution still stays service-only."
                    : $"Live service scan completed across {currentScan.Volumes.Count:N0} mounted volumes with {currentScan.Duplicates.Count:N0} duplicate clusters.";

                StatusLine = liveResponse is null
                    ? "Preview scan complete. Atlas can keep planning safely while the service is offline."
                    : "Live scan complete. Fresh inventory is ready for planning.";

                AddActivity("Scan complete", InventorySummary);
                ApplyCommandPreview(
                    currentScan.Inventory.Count == 0
                        ? "Atlas refreshed the mutable roots but did not find files eligible for reorganization."
                        : "Atlas refreshed the mutable workspace inventory and is ready for the next request.",
                    InventorySummary,
                    liveResponse is null ? "Preview scan" : "Live scan",
                    "Command Deck",
                    "Inventory first",
                    "Scanning remains read-only. Atlas still needs a reviewed plan before any mutation can even become eligible.",
                    currentScan.Inventory.Count == 0 ? "Inspect mutable roots" : "Draft a reversible plan",
                    currentScan.Inventory.Count == 0
                        ? "Check the approved roots in Policy Studio or adjust the request scope before drafting a plan."
                        : "The inventory is fresh now. Route into Plan Review to shape a safer organization pass.");
            });
    }

    public async Task DraftPlanAsync(string? intent)
    {
        var requestedIntent = string.IsNullOrWhiteSpace(intent)
            ? "Organize the mutable user roots into clearer categories, review duplicates conservatively, and keep everything reversible."
            : intent.Trim();

        await ExecuteGuardedAsync(
            "Drafting a reversible plan...",
            async cancellationToken =>
            {
                if (currentScan.Inventory.Count == 0)
                {
                    currentScan = await Task.Run(() => BuildPreviewScan(cancellationToken), cancellationToken);
                    ApplyScanState();
                }

                var liveResponse = await TryPipeCallAsync<PlanRequest, PlanResponse>(
                    "plan/request",
                    new PlanRequest
                    {
                        UserIntent = requestedIntent,
                        Scan = currentScan,
                        PolicyProfile = profile
                    },
                    cancellationToken);

                currentPlan = liveResponse ?? BuildPreviewPlan(requestedIntent);
                ApplyPlanState();

                if (liveResponse is not null)
                {
                    await RefreshPersistedHistoryAsync(cancellationToken);
                }

                CurrentFocus = $"Intent locked: {requestedIntent}";
                StatusLine = liveResponse is null
                    ? "Preview plan ready. Review the diff before asking the service to execute anything."
                    : "Live plan ready. The service has re-validated the risk envelope.";

                AddActivity("Plan drafted", currentPlan.Summary);
                ApplyCommandPreview(
                    "A reversible organization plan is ready for inspection.",
                    currentPlan.Summary,
                    liveResponse is null ? "Preview planning" : "Live planning",
                    "Plan Review",
                    currentPlan.Plan.RequiresReview ? "Review gates active" : "Plan preview ready",
                    RiskSummary,
                    "Inspect the diff canvas",
                    "Compare the current and proposed structure, then preview execution before asking the service to touch anything.");
            });
    }

    public async Task AnalyzeOptimizationAsync()
    {
        await ExecuteGuardedAsync(
            "Inspecting safe optimization pressure...",
            async cancellationToken =>
            {
                var liveResponse = await TryPipeCallAsync<OptimizationRequest, OptimizationResponse>(
                    "optimize/request",
                    new OptimizationRequest
                    {
                        IncludeRecommendationsOnly = true,
                        PolicyProfile = profile
                    },
                    cancellationToken);

                currentOptimization = liveResponse ?? await Task.Run(() => BuildPreviewOptimizationResponse(cancellationToken), cancellationToken);
                ApplyOptimizationState();

                if (liveResponse is not null)
                {
                    await RefreshPersistedHistoryAsync(cancellationToken);
                }

                StatusLine = liveResponse is null
                    ? "Preview optimization scan complete. Recommendations are review-only until the service is connected."
                    : "Optimization scan complete. Safe fixes and manual recommendations are ready to review.";

                AddActivity("Optimization scan complete", OptimizationSummary);
                ApplyCommandPreview(
                    "Safe optimization findings are staged for review.",
                    OptimizationSummary,
                    liveResponse is null ? "Preview optimization" : "Live optimization",
                    "Optimization Center",
                    "Evidence first",
                    "Curated low-risk fixes can be reviewed alongside recommendation-only findings. Unsafe tweak folklore stays out.",
                    "Inspect optimization details",
                    "Review the safe-fix, approval-gated, and recommendation-only groups before deciding what should run.");
            });
    }

    public async Task PreviewExecutionAsync()
    {
        if (!HasPlan)
        {
            return;
        }

        await ExecuteGuardedAsync(
            "Preparing an execution dry run...",
            async cancellationToken =>
            {
                var batch = BuildBatchFromCurrentPlan(isDryRun: true);
                var liveResponse = await TryPipeCallAsync<ExecutionRequest, ExecutionResponse>(
                    "execute/request",
                    new ExecutionRequest
                    {
                        Batch = batch,
                        PolicyProfile = profile,
                        Execute = false
                    },
                    cancellationToken);

                var response = liveResponse ?? BuildPreviewExecutionResponse(batch);
                latestCheckpoint = response.UndoCheckpoint;
                ApplyUndoState();

                StatusLine = liveResponse is null
                    ? "Preview dry run complete. Atlas simulated the recovery story locally."
                    : "Live dry run complete. The service staged rollback metadata without mutating files.";

                AddActivity("Execution preview", string.Join(" ", response.Messages.Take(2)));
                ApplyCommandPreview(
                    "Execution preview complete. Atlas has a rollback story ready to inspect.",
                    string.Join(" ", response.Messages.Take(2)),
                    liveResponse is null ? "Preview dry run" : "Live dry run",
                    "Plan Review",
                    "Rollback story staged",
                    UndoSummary,
                    "Inspect Undo Timeline",
                    "Review the inverse operations and quarantine entries before deciding whether to submit the real batch.");
            });
    }

    public async Task ExecutePlanAsync()
    {
        if (!HasPlan)
        {
            return;
        }

        await ExecuteGuardedAsync(
            "Submitting the plan to the service...",
            async cancellationToken =>
            {
                if (!IsLiveMode)
                {
                    StatusLine = "Execution is blocked in preview mode. Connect the service before mutating anything.";
                    AddActivity("Execution blocked", StatusLine);
                    return;
                }

                var batch = BuildBatchFromCurrentPlan(isDryRun: false);
                var response = await TryPipeCallAsync<ExecutionRequest, ExecutionResponse>(
                    "execute/request",
                    new ExecutionRequest
                    {
                        Batch = batch,
                        PolicyProfile = profile,
                        Execute = true
                    },
                    cancellationToken);

                if (response is null)
                {
                    StatusLine = "The service dropped offline before execution could start. No filesystem changes were made.";
                    AddActivity("Execution interrupted", StatusLine);
                    return;
                }

                latestCheckpoint = response.UndoCheckpoint;
                ApplyUndoState();

                if (response.Success)
                {
                    await RefreshPersistedHistoryAsync(cancellationToken);
                }

                StatusLine = response.Success
                    ? "Execution completed through the service. Undo metadata is now available."
                    : "Execution was blocked by local safety checks in the service.";

                AddActivity(response.Success ? "Plan executed" : "Execution blocked", string.Join(" ", response.Messages.Take(3)));
                ApplyCommandPreview(
                    response.Success
                        ? "The service finished the latest approved batch and recorded recovery metadata."
                        : "The service refused or interrupted the latest execution request.",
                    string.Join(" ", response.Messages.Take(3)),
                    "Service execution",
                    response.Success ? "Undo Timeline" : "Plan Review",
                    response.Success ? "Recovery armed" : "Service blocked execution",
                    response.Success
                        ? UndoSummary
                        : "No further actions should be attempted until the blocked reasons and review gates are understood.",
                    response.Success ? "Review Undo Timeline" : "Return to Plan Review",
                    response.Success
                        ? "Use the undo workspace to inspect the new checkpoint and verify restore posture."
                        : "Inspect the latest plan, risk summary, and guardrails before retrying.");
            });
    }

    public async Task PreviewUndoAsync()
    {
        if (!HasUndoCheckpoint)
        {
            return;
        }

        await ExecuteGuardedAsync(
            "Previewing the rollback path...",
            async cancellationToken =>
            {
                var liveResponse = await TryPipeCallAsync<UndoRequest, UndoResponse>(
                    "undo/request",
                    new UndoRequest
                    {
                        Checkpoint = latestCheckpoint,
                        Execute = false
                    },
                    cancellationToken);

                var response = liveResponse ?? new UndoResponse
                {
                    Success = true,
                    Messages =
                    [
                        $"Previewed {latestCheckpoint.InverseOperations.Count:N0} inverse operations.",
                        $"Tracked {latestCheckpoint.QuarantineItems.Count:N0} quarantined items ready for restore."
                    ]
                };

                StatusLine = liveResponse is null
                    ? "Preview undo complete. Atlas simulated the rollback locally."
                    : "Live undo preview complete. The service confirmed the rollback path.";

                AddActivity("Undo preview", string.Join(" ", response.Messages.Take(2)));
                ApplyCommandPreview(
                    "Rollback preview complete. Atlas confirmed the current undo path.",
                    string.Join(" ", response.Messages.Take(2)),
                    liveResponse is null ? "Preview undo" : "Live undo preview",
                    "Undo Timeline",
                    "Checkpoint review",
                    UndoSummary,
                    "Inspect the rollback details",
                    "Review the inverse operations and quarantined items, then decide whether the real undo should run.");
            });
    }

    public async Task ExecuteUndoAsync()
    {
        if (!HasUndoCheckpoint)
        {
            return;
        }

        await ExecuteGuardedAsync(
            "Submitting the rollback to the service...",
            async cancellationToken =>
            {
                if (!IsLiveMode)
                {
                    StatusLine = "Undo execution is blocked in preview mode. The service is required to replay rollback operations.";
                    AddActivity("Undo blocked", StatusLine);
                    return;
                }

                var response = await TryPipeCallAsync<UndoRequest, UndoResponse>(
                    "undo/request",
                    new UndoRequest
                    {
                        Checkpoint = latestCheckpoint,
                        Execute = true
                    },
                    cancellationToken);

                if (response is null)
                {
                    StatusLine = "Undo could not reach the service. No rollback steps were applied.";
                    AddActivity("Undo interrupted", StatusLine);
                    return;
                }

                StatusLine = response.Success
                    ? "Rollback completed through the service."
                    : "Rollback failed. Review the latest activity details before retrying.";

                AddActivity(response.Success ? "Undo executed" : "Undo failed", string.Join(" ", response.Messages.Take(3)));
                ApplyCommandPreview(
                    response.Success
                        ? "The service replayed the rollback steps for the selected checkpoint."
                        : "The rollback attempt needs attention before Atlas should try again.",
                    string.Join(" ", response.Messages.Take(3)),
                    "Service rollback",
                    "Undo Timeline",
                    response.Success ? "Rollback complete" : "Rollback attention required",
                    response.Success
                        ? "Atlas finished the requested recovery path. Review the recent activity and quarantine state to confirm the machine landed cleanly."
                        : "Atlas could not complete the recovery path. Check the activity feed and latest checkpoint notes before retrying.",
                    "Review recent activity",
                    "Use the Command Deck and Undo Timeline together to confirm the current state before issuing another destructive request.");
            });
    }

    public async Task<VoiceIntentResponse> PreviewVoiceIntentAsync(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return new VoiceIntentResponse
            {
                ParsedIntent = "No transcript captured yet.",
                NeedsConfirmation = true
            };
        }

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var liveResponse = await TryPipeCallAsync<VoiceIntentRequest, VoiceIntentResponse>(
            "voice/request",
            new VoiceIntentRequest { Transcript = transcript },
            cancellationSource.Token);

        return liveResponse ?? new VoiceIntentResponse
        {
            ParsedIntent = transcript.Trim(),
            NeedsConfirmation = true
        };
    }

    public void UpdateCommandDraftPreview(string? draft, string currentSectionTag, string origin = "Typed draft", bool? requiresConfirmation = null)
    {
        var normalized = draft?.Trim() ?? string.Empty;
        var routeTag = string.IsNullOrWhiteSpace(normalized)
            ? currentSectionTag
            : ResolveCommandRouteTag(normalized, currentSectionTag);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            ApplyCommandPreview(
                routeTag switch
                {
                    "optimization" => "Inspect safe optimization pressure without drifting into unsafe tweaks.",
                    "undo" => "Preview rollback and restore options before you need them.",
                    "settings" => "Review policy and guardrails without weakening the safety boundary.",
                    "plans" => "Draft a reversible organization request and review it before execution.",
                    _ => "Atlas is waiting for a guarded request and keeping the trust boundary visible."
                },
                routeTag switch
                {
                    "optimization" => "Ask Atlas about startup clutter, cache buildup, temporary files, or other approved optimization classes.",
                    "undo" => "Request a rollback preview, a quarantined-file restore, or the latest recovery story.",
                    "settings" => "Review roots, thresholds, sync-folder handling, and upload posture without crossing into protected system space.",
                    "plans" => "Describe the outcome you want, such as cleaning Downloads or consolidating screenshots into clearer anchors.",
                    _ => "Type a request like 'organize Downloads', 'inspect temp buildup', or 'restore the last quarantined PDF'."
                },
                origin,
                ResolveWorkspaceLabel(routeTag),
                routeTag switch
                {
                    "optimization" => "Evidence first",
                    "undo" => "Checkpoint preview",
                    "settings" => "Policy stays explicit",
                    _ => "Preview before execute"
                },
                routeTag switch
                {
                    "optimization" => "Optimization requests stay bounded to curated safe classes with evidence and rollback guidance.",
                    "undo" => "Rollback remains service-only and replays from checkpoint data instead of ad hoc file operations.",
                    "settings" => "Atlas lets you tune policy, but protected roots and service-side guardrails remain in force.",
                    _ => "Atlas drafts the plan first, then keeps deletion quarantine-first and execution blocked until the service is live."
                },
                routeTag switch
                {
                    "optimization" => "Run an optimization analysis",
                    "undo" => "Preview the latest undo path",
                    "settings" => "Review the current policy",
                    "plans" => "Draft the organization plan",
                    _ => "Run a fresh scan"
                },
                BuildNextStepDetail(routeTag, requiresConfirmation == true));
            return;
        }

        var escalatesGuardrails = ContainsAny(normalized, "system32", "c:\\windows", "program files", "program files (x86)", "appdata", "boot", "registry", "driver");
        var soundsDestructive = requiresConfirmation == true || ContainsAny(normalized, "delete", "remove", "purge", "wipe", "erase", "clean", "reorganize", "move", "archive");

        var reviewLabel = escalatesGuardrails
            ? "Guardrail escalation"
            : soundsDestructive
                ? "Confirmation required"
                : routeTag switch
                {
                    "optimization" => "Evidence first",
                    "undo" => "Checkpoint review",
                    "settings" => "Policy review",
                    _ => "Plan preview"
                };

        var reviewDetail = escalatesGuardrails
            ? "This language points at protected or system-adjacent areas. Atlas will keep those paths blocked and route the request through explicit review."
            : soundsDestructive
                ? "The request sounds destructive or structural, so Atlas will keep it in preview mode with quarantine-first recovery where possible."
                : routeTag switch
                {
                    "optimization" => "Atlas will surface evidence, projected impact, and rollback posture before any approved fix is eligible.",
                    "undo" => "Atlas will confirm the checkpoint story first so restores and reversals remain deliberate.",
                    "settings" => "Policy edits stay legible and reversible, with the hard safety boundary still enforced by the service.",
                    _ => "Atlas will convert this into a structured plan and surface the diff, rationale, and risk envelope before execution."
                };

        ApplyCommandPreview(
            routeTag switch
            {
                "optimization" => "Analyze safe optimization opportunities",
                "undo" => "Prepare a rollback or restore preview",
                "settings" => "Inspect policy and safety thresholds",
                _ => "Draft a reversible organization plan"
            },
            normalized.Length <= 160 ? normalized : $"{normalized[..157]}...",
            origin,
            ResolveWorkspaceLabel(routeTag),
            reviewLabel,
            reviewDetail,
            routeTag switch
            {
                "optimization" => "Route into Optimization Center",
                "undo" => "Route into Undo Timeline",
                "settings" => "Route into Policy Studio",
                _ => "Route into Plan Review"
            },
            BuildNextStepDetail(routeTag, soundsDestructive));
    }

    public void SavePolicyChanges()
    {
        StatusLine = $"Policy updated. Sync exclusion is {(ExcludeSyncFoldersByDefault ? "on" : "off")}, duplicate auto-quarantine is {DuplicateConfidenceThreshold:P1}, retention is {QuarantineRetentionLabel}.";
        SyncProfileCollections();
        AddActivity("Policy updated", StatusLine);
        ApplyCommandPreview(
            "Policy changes captured without weakening the service boundary.",
            StatusLine,
            "Policy studio",
            "Policy Studio",
            "Policy review",
            "Protected paths and service-side enforcement still remain active after local policy edits.",
            "Return to the workspace",
            "Review the updated guardrails, then draft a plan or optimization pass against the new settings.");
    }

    public void ResetPolicy()
    {
        profile = PolicyProfileFactory.CreateDefault();
        QuarantineRetentionDays = 30;
        SyncProfileCollections();

        OnPropertyChanged(nameof(PolicyProfile));
        OnPropertyChanged(nameof(ExcludeSyncFoldersByDefault));
        OnPropertyChanged(nameof(UploadSensitiveContent));
        OnPropertyChanged(nameof(AutoFixLowRiskOptimizations));
        OnPropertyChanged(nameof(DuplicateConfidenceThreshold));
        OnPropertyChanged(nameof(DuplicateConfidenceThresholdLabel));

        StatusLine = "Policy reset to the guarded Windows 11 consumer defaults.";
        AddActivity("Policy reset", StatusLine);
        ApplyCommandPreview(
            "Atlas is back on the guarded default profile.",
            "Sync folders stay excluded, destructive work stays reversible, and protected roots remain blocked by default.",
            "Policy studio",
            "Policy Studio",
            "Defaults restored",
            "The shell can keep exploring, but the service still decides whether any filesystem change is allowed.",
            "Run a fresh scan",
            "Use the refreshed defaults to rescan the mutable roots or draft a safer organization request.");
    }

    public IReadOnlyList<string> GetSuggestedCommands(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return CommandSuggestions.Take(6).ToList();
        }

        return CommandSuggestions
            .Where(suggestion => suggestion.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToList();
    }

    private async Task ExecuteGuardedAsync(string busyMessage, Func<CancellationToken, Task> action)
    {
        await operationLock.WaitAsync();
        try
        {
            IsBusy = true;
            StatusLine = busyMessage;
            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await action(cancellationSource.Token);
        }
        finally
        {
            IsBusy = false;
            operationLock.Release();
        }
    }

    private async Task<TResponse?> TryPipeCallAsync<TRequest, TResponse>(string messageType, TRequest payload, CancellationToken cancellationToken)
    {
        try
        {
            var response = await pipeClient.RoundTripAsync<TRequest, TResponse>(messageType, payload, cancellationToken);
            SetConnectionMode(isLiveMode: true);
            return response;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            SetConnectionMode(isLiveMode: false);
            return default;
        }
    }

    private async Task RefreshPersistedHistoryAsync(CancellationToken cancellationToken)
    {
        var snapshot = await TryPipeCallAsync<HistorySnapshotRequest, HistorySnapshotResponse>(
            "history/snapshot",
            new HistorySnapshotRequest { Limit = 6 },
            cancellationToken);

        if (snapshot is null)
        {
            persistedHistory = new();
            persistedInventorySnapshot = new();
            persistedInventorySessions = new();
            persistedInventoryDetail = new();
            persistedInventoryFiles = new();
            persistedDriftSnapshot = new();
            persistedSessionDiff = new();
            persistedDiffFiles = new();
            ReplaceAll(PersistedScanSessionMemory, []);
            ReplaceAll(PersistedVolumeMemory, []);
            ReplaceAll(PersistedFileSampleMemory, []);
            ReplaceAll(ScanContinuitySignals, []);
            ReplaceAll(RescanStoryCards, []);
            ReplaceAll(DriftReviewCards, []);
            ReplaceAll(ScanPairSignals, []);
            ReplaceAll(DriftFileSampleCards, []);
            ReplaceAll(DriftHotspotCards, []);
            ReplaceAll(ScanProvenanceSignals, []);
            ReplaceAll(PersistedPlanMemory, []);
            ReplaceAll(PersistedFindingMemory, []);
            ReplaceAll(PersistedTraceMemory, []);
            ScanContinuitySummaryText = "Scan continuity will appear here once Atlas can compare persisted sessions.";
            RescanStorySummaryText = "Rescan story will appear here once Atlas can compare stored sessions.";
            DriftReviewSummaryText = "Drift review will appear here once Atlas can compare stored scan sessions more explicitly.";
            ScanPairSummaryText = "Stored session-pair intelligence will appear here once Atlas can compare two persisted scans.";
            DriftFileSampleSummaryText = "A bounded changed-path sample will appear here once Atlas can compare stored scan pairs.";
            DriftHotspotSummaryText = "Drift hotspots will appear here once Atlas can summarize changed-file patterns.";
            ScanProvenanceSummaryText = "Scan provenance will appear here once Atlas has enough live or stored scan evidence to explain session origin.";
            PersistedMemorySummaryText = "Atlas could not refresh stored memory because the service is unavailable.";
            PersistedScanSummaryText = "Stored scan sessions will appear here once the service can answer inventory history queries.";
            PersistedVolumeSummaryText = "Stored volume posture will appear here once Atlas can inspect a persisted scan session.";
            PersistedFileSampleSummaryText = "Stored file samples will appear here once Atlas can inspect persisted inventory rows.";
            PersistedPlanSummaryText = "Stored plan drafts will appear here once the service can answer history queries.";
            PersistedFindingSummaryText = "Stored optimization findings will appear here once the service can answer history queries.";
            PersistedTraceSummaryText = "Stored planning and voice traces will appear here once the service can answer history queries.";
            RebuildRecoveryCollections();
            RefreshInventorySignals();
            RefreshInventoryMemoryCards();
            RefreshHistoryMetrics();
            return;
        }

        persistedHistory = snapshot;
        await RefreshPersistedInventoryAsync(cancellationToken);

        ReplaceAll(PersistedScanSessionMemory, persistedInventorySessions.Sessions.Select(CreateStoredScanSessionCard));
        ReplaceAll(PersistedVolumeMemory, persistedInventoryDetail.Volumes.Select(CreateStoredVolumeCard));
        ReplaceAll(PersistedFileSampleMemory, persistedInventoryFiles.Files.Select(CreateStoredFileCard));
        ReplaceAll(PersistedPlanMemory, persistedHistory.RecentPlans.Select(CreateStoredPlanCard));
        ReplaceAll(PersistedFindingMemory, persistedHistory.RecentFindings.Select(CreateStoredFindingCard));
        ReplaceAll(PersistedTraceMemory, persistedHistory.RecentTraces.Select(CreateStoredTraceCard));

        PersistedMemorySummaryText = persistedInventorySnapshot.HasSession
            ? $"{persistedInventorySessions.Sessions.Count:N0} stored scans, {persistedHistory.RecentPlans.Count:N0} plans, {persistedHistory.RecentCheckpoints.Count:N0} checkpoints, {persistedHistory.RecentQuarantine.Count:N0} quarantine items, {persistedHistory.RecentFindings.Count:N0} findings, and {persistedHistory.RecentTraces.Count:N0} traces are available from stored service memory."
            : $"{persistedHistory.RecentPlans.Count:N0} plans, {persistedHistory.RecentCheckpoints.Count:N0} checkpoints, {persistedHistory.RecentQuarantine.Count:N0} quarantine items, {persistedHistory.RecentFindings.Count:N0} findings, and {persistedHistory.RecentTraces.Count:N0} traces are available from stored service memory.";
        if (persistedDriftSnapshot.HasBaseline)
        {
            PersistedMemorySummaryText += $" The latest scan pair shows {persistedDriftSnapshot.AddedCount:N0} added, {persistedDriftSnapshot.RemovedCount:N0} removed, and {persistedDriftSnapshot.ChangedCount:N0} changed paths.";
        }
        PersistedScanSummaryText = PersistedScanSessionMemory.Count == 0
            ? "No stored scan sessions are available yet."
            : string.Join("\n\n", PersistedScanSessionMemory.Take(3).Select(card => $"{card.Eyebrow}: {card.Title}\n{card.Detail}"));
        PersistedVolumeSummaryText = PersistedVolumeMemory.Count == 0
            ? "No stored volume posture is available yet."
            : string.Join("\n\n", PersistedVolumeMemory.Take(3).Select(card => $"{card.Eyebrow}: {card.Title}\n{card.Detail}"));
        PersistedFileSampleSummaryText = PersistedFileSampleMemory.Count == 0
            ? "No stored file sample is available yet."
            : string.Join("\n\n", PersistedFileSampleMemory.Take(3).Select(card => $"{card.Eyebrow}: {card.Title}\n{card.Detail}"));
        PersistedPlanSummaryText = PersistedPlanMemory.Count == 0
            ? "No stored plans are available yet."
            : string.Join("\n\n", PersistedPlanMemory.Take(3).Select(card => $"{card.Eyebrow}: {card.Title}\n{card.Detail}"));
        PersistedFindingSummaryText = PersistedFindingMemory.Count == 0
            ? "No stored optimization findings are available yet."
            : string.Join("\n\n", PersistedFindingMemory.Take(3).Select(card => $"{card.Eyebrow}: {card.Title}\n{card.Detail}"));
        PersistedTraceSummaryText = PersistedTraceMemory.Count == 0
            ? "No stored prompt traces are available yet."
            : string.Join("\n\n", PersistedTraceMemory.Take(3).Select(card => $"{card.Eyebrow}: {card.Title}\n{card.Detail}"));

        RefreshScanContinuitySignals();
        RefreshRescanStoryCards();
        RefreshDriftReviewCards();
        RefreshScanPairSignals();
        RefreshDriftFileSampleCards();
        RefreshDriftHotspotCards();
        RefreshScanProvenanceSignals();
        RebuildRecoveryCollections();
        RefreshInventorySignals();
        RefreshInventoryMemoryCards();
        RefreshHistoryMetrics();
    }

    private async Task RefreshPersistedInventoryAsync(CancellationToken cancellationToken)
    {
        var snapshot = await TryPipeCallAsync<InventorySnapshotRequest, InventorySnapshotResponse>(
            "inventory/snapshot",
            new InventorySnapshotRequest { Limit = 1 },
            cancellationToken);

        if (snapshot is null)
        {
            persistedInventorySnapshot = new();
            persistedInventorySessions = new();
            persistedInventoryDetail = new();
            persistedInventoryFiles = new();
            persistedDriftSnapshot = new();
            persistedSessionDiff = new();
            persistedDiffFiles = new();
            return;
        }

        persistedInventorySnapshot = snapshot;
        persistedInventorySessions = await TryPipeCallAsync<InventorySessionListRequest, InventorySessionListResponse>(
            "inventory/sessions",
            new InventorySessionListRequest { Limit = 6 },
            cancellationToken) ?? new InventorySessionListResponse();

        if (!snapshot.HasSession || string.IsNullOrWhiteSpace(snapshot.SessionId))
        {
            persistedInventoryDetail = new();
            persistedInventoryFiles = new();
            persistedDriftSnapshot = new();
            persistedSessionDiff = new();
            persistedDiffFiles = new();
            return;
        }

        persistedInventoryDetail = await TryPipeCallAsync<InventorySessionDetailRequest, InventorySessionDetailResponse>(
            "inventory/session-detail",
            new InventorySessionDetailRequest { SessionId = snapshot.SessionId },
            cancellationToken) ?? new InventorySessionDetailResponse();

        persistedInventoryFiles = await TryPipeCallAsync<InventoryFileListRequest, InventoryFileListResponse>(
            "inventory/files",
            new InventoryFileListRequest
            {
                SessionId = snapshot.SessionId,
                Limit = 12
            },
            cancellationToken) ?? new InventoryFileListResponse();

        await RefreshPersistedDriftAsync(cancellationToken);
    }

    private async Task RefreshPersistedDriftAsync(CancellationToken cancellationToken)
    {
        persistedDriftSnapshot = await TryPipeCallAsync<DriftSnapshotRequest, DriftSnapshotResponse>(
            "inventory/drift-snapshot",
            new DriftSnapshotRequest(),
            cancellationToken) ?? new DriftSnapshotResponse();

        if (!persistedDriftSnapshot.HasBaseline
            || string.IsNullOrWhiteSpace(persistedDriftSnapshot.OlderSessionId)
            || string.IsNullOrWhiteSpace(persistedDriftSnapshot.NewerSessionId))
        {
            persistedSessionDiff = new();
            persistedDiffFiles = new();
            return;
        }

        persistedSessionDiff = await TryPipeCallAsync<SessionDiffRequest, SessionDiffResponse>(
            "inventory/session-diff",
            new SessionDiffRequest
            {
                OlderSessionId = persistedDriftSnapshot.OlderSessionId,
                NewerSessionId = persistedDriftSnapshot.NewerSessionId
            },
            cancellationToken) ?? new SessionDiffResponse();

        persistedDiffFiles = await TryPipeCallAsync<SessionDiffFilesRequest, SessionDiffFilesResponse>(
            "inventory/session-diff-files",
            new SessionDiffFilesRequest
            {
                OlderSessionId = persistedDriftSnapshot.OlderSessionId,
                NewerSessionId = persistedDriftSnapshot.NewerSessionId,
                Limit = 12
            },
            cancellationToken) ?? new SessionDiffFilesResponse();
    }

    private void RebuildRecoveryCollections()
    {
        var undoBatches = new List<AtlasUndoBatchCard>();
        var quarantineItems = new List<AtlasQuarantineCard>();

        if (latestCheckpoint.InverseOperations.Count > 0 || latestCheckpoint.QuarantineItems.Count > 0)
        {
            undoBatches.Add(new AtlasUndoBatchCard(
                DateTime.Now.ToString("g"),
                $"{latestCheckpoint.InverseOperations.Count:N0} inverse ops",
                latestCheckpoint.Notes.Count > 0
                    ? string.Join(" ", latestCheckpoint.Notes)
                    : "Recovery metadata is staged for the latest batch."));

            quarantineItems.AddRange(latestCheckpoint.QuarantineItems.Select(item =>
                new AtlasQuarantineCard(
                    Path.GetFileName(item.OriginalPath),
                    item.OriginalPath,
                    item.RetentionUntilUnixTimeSeconds > 0
                        ? $"Restorable until {DateTimeOffset.FromUnixTimeSeconds(item.RetentionUntilUnixTimeSeconds).LocalDateTime:g}"
                        : "Restorable while retained in quarantine")));
        }

        var existingBatchIds = new HashSet<string>(
            latestCheckpoint.BatchId == string.Empty ? [] : [latestCheckpoint.BatchId],
            StringComparer.OrdinalIgnoreCase);

        undoBatches.AddRange(persistedHistory.RecentCheckpoints
            .Where(checkpoint => !existingBatchIds.Contains(checkpoint.BatchId))
            .Select(CreateStoredCheckpointCard));

        var existingPaths = latestCheckpoint.QuarantineItems
            .Select(item => item.OriginalPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        quarantineItems.AddRange(persistedHistory.RecentQuarantine
            .Where(item => !existingPaths.Contains(item.OriginalPath))
            .Select(CreateStoredQuarantineCard));

        ReplaceAll(UndoBatches, undoBatches);
        ReplaceAll(QuarantineEntries, quarantineItems);
    }

    private static AtlasStoredMemoryCard CreateStoredPlanCard(HistoryPlanSummary plan) =>
        new(
            FormatHistoryTimestamp(plan.CreatedUtc),
            string.IsNullOrWhiteSpace(plan.Scope) ? "Stored plan" : plan.Scope,
            string.IsNullOrWhiteSpace(plan.Summary) ? $"Plan {ShortId(plan.PlanId)} is available from stored history." : plan.Summary);

    private static AtlasStoredMemoryCard CreateStoredFindingCard(HistoryFindingSummary finding) =>
        new(
            FormatHistoryTimestamp(finding.CreatedUtc),
            finding.Kind.ToString(),
            $"{finding.Target}\n{(finding.CanAutoFix ? "Auto-fix ready when approved." : "Recommendation-only evidence retained in history.")}");

    private static AtlasStoredMemoryCard CreateStoredTraceCard(HistoryTraceSummary trace) =>
        new(
            FormatHistoryTimestamp(trace.CreatedUtc),
            string.IsNullOrWhiteSpace(trace.Stage) ? "Prompt trace" : trace.Stage.Replace('_', ' '),
            $"Trace {ShortId(trace.TraceId)} is available for later drill-in once detail routes are added.");

    private static AtlasStoredMemoryCard CreateStoredScanSessionCard(InventorySessionSummary session) =>
        new(
            FormatHistoryTimestamp(session.CreatedUtc),
            $"{session.FilesScanned:N0} files in {session.RootCount:N0} roots",
            $"{session.DuplicateGroupCount:N0} duplicate groups across {session.VolumeCount:N0} volumes. "
            + $"{BuildStoredSessionProvenanceDetail(session.Trigger, session.BuildMode, session.DeltaSource, session.BaselineSessionId, session.IsTrusted, session.CompositionNote)} "
            + $"Session {ShortId(session.SessionId)} remains available for scan drift and later diff review.");

    private static AtlasStoredMemoryCard CreateStoredVolumeCard(InventoryVolumeSummary volume)
    {
        var usagePercent = volume.TotalSizeBytes <= 0
            ? 0
            : (1d - ((double)volume.FreeSpaceBytes / volume.TotalSizeBytes)) * 100d;

        return new AtlasStoredMemoryCard(
            volume.DriveType,
            $"{volume.RootPath} ({volume.DriveFormat})",
            volume.IsReady
                ? $"{FormatBytes(volume.FreeSpaceBytes)} free of {FormatBytes(volume.TotalSizeBytes)}. Approx. {usagePercent:0}% used in the latest stored scan."
                : "This volume was present in the stored scan but reported as not ready.");
    }

    private static AtlasStoredMemoryCard CreateStoredFileCard(InventoryFileSummary file)
    {
        var sensitivity = file.Sensitivity >= SensitivityLevel.High
            ? "Sensitive"
            : file.Sensitivity == SensitivityLevel.Medium
                ? "Watched"
                : "Routine";

        return new AtlasStoredMemoryCard(
            string.IsNullOrWhiteSpace(file.Category) ? "Other" : file.Category,
            file.Name,
            $"{file.Extension} • {FormatBytes(file.SizeBytes)} • {sensitivity}{(file.IsSyncManaged ? " • sync-managed" : string.Empty)}{(file.IsDuplicateCandidate ? " • duplicate candidate" : string.Empty)}");
    }

    private static string BuildStoredFileSampleDetail(IEnumerable<InventoryFileSummary> files)
    {
        var sample = files.ToList();
        if (sample.Count == 0)
        {
            return "Atlas will describe the persisted file shape after the next stored scan arrives.";
        }

        var categorySummary = string.Join(", ", sample
            .GroupBy(file => string.IsNullOrWhiteSpace(file.Category) ? "Other" : file.Category)
            .OrderByDescending(group => group.Count())
            .Take(3)
            .Select(group => $"{group.Key} {group.Count():N0}"));

        var sensitiveCount = sample.Count(file => file.Sensitivity >= SensitivityLevel.High);
        return sensitiveCount > 0
            ? $"{categorySummary}. {sensitiveCount:N0} high-sensitivity items appear inside the latest stored file sample."
            : $"{categorySummary}. Sample pulled from the latest persisted scan session.";
    }

    private static AtlasUndoBatchCard CreateStoredCheckpointCard(HistoryCheckpointSummary checkpoint) =>
        new(
            FormatHistoryTimestamp(checkpoint.CreatedUtc),
            $"{checkpoint.OperationCount:N0} stored inverse ops",
            $"Batch {ShortId(checkpoint.BatchId)} remains available in persisted recovery history.");

    private static AtlasQuarantineCard CreateStoredQuarantineCard(HistoryQuarantineSummary item) =>
        new(
            Path.GetFileName(item.OriginalPath),
            item.OriginalPath,
            $"Stored quarantine memory. Reason: {item.Reason}. Retained until {FormatHistoryTimestamp(item.RetentionUntilUtc)}.");

    private static AtlasStoredMemoryCard CreateDriftFileSampleCard(DiffFileSummary file)
    {
        var title = Path.GetFileName(file.Path);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = file.Path;
        }

        var detail = file.ChangeKind switch
        {
            "Added" => $"{file.Path}\nNew path at {FormatBytes(file.NewerSizeBytes)}. Observed {FormatUnixTimestamp(file.NewerLastModifiedUnix)}.",
            "Removed" => $"{file.Path}\nPreviously {FormatBytes(file.OlderSizeBytes)}. Last seen {FormatUnixTimestamp(file.OlderLastModifiedUnix)}.",
            "Changed" => $"{file.Path}\n{DescribeChangedFile(file)}",
            _ => file.Path
        };

        return new AtlasStoredMemoryCard(file.ChangeKind.ToUpperInvariant(), title, detail);
    }

    private static AtlasStoredMemoryCard CreateDiffEvidenceCard(
        string eyebrow,
        int count,
        IReadOnlyList<DiffFileSummary> files,
        string changeKind,
        string zeroTitle,
        string zeroDetail)
    {
        if (count == 0)
        {
            return new AtlasStoredMemoryCard(eyebrow, zeroTitle, zeroDetail);
        }

        var matching = files
            .Where(file => string.Equals(file.ChangeKind, changeKind, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        var title = $"{count:N0} {changeKind.ToLowerInvariant()} path{(count == 1 ? string.Empty : "s")}";
        var detail = matching.Count == 0
            ? $"Atlas counted {count:N0} {changeKind.ToLowerInvariant()} path{(count == 1 ? string.Empty : "s")}, but the current bounded evidence page did not include a sample row."
            : BuildDiffEvidenceDetail(matching, changeKind, count);

        return new AtlasStoredMemoryCard(eyebrow, title, detail);
    }

    private static string BuildDiffEvidenceDetail(IReadOnlyList<DiffFileSummary> files, string changeKind, int totalCount)
    {
        var samples = string.Join("\n", files.Select(file => DescribeDiffFile(file, changeKind)));
        var remainder = totalCount - files.Count;
        return remainder > 0
            ? $"{samples}\n{remainder:N0} more {changeKind.ToLowerInvariant()} paths remain outside the current bounded sample."
            : samples;
    }

    private static string DescribeDiffFile(DiffFileSummary file, string changeKind)
    {
        return changeKind switch
        {
            "Added" => $"{file.Path} ({FormatBytes(file.NewerSizeBytes)}, {FormatUnixTimestamp(file.NewerLastModifiedUnix)})",
            "Removed" => $"{file.Path} (was {FormatBytes(file.OlderSizeBytes)}, {FormatUnixTimestamp(file.OlderLastModifiedUnix)})",
            "Changed" => $"{file.Path} ({DescribeChangedFile(file)})",
            _ => file.Path
        };
    }

    private static string DescribeChangedFile(DiffFileSummary file)
    {
        var sizeChanged = file.OlderSizeBytes != file.NewerSizeBytes;
        var modifiedChanged = file.OlderLastModifiedUnix != file.NewerLastModifiedUnix;

        if (sizeChanged && modifiedChanged)
        {
            return $"{FormatBytes(file.OlderSizeBytes)} -> {FormatBytes(file.NewerSizeBytes)}, updated {FormatUnixTimestamp(file.NewerLastModifiedUnix)}";
        }

        if (sizeChanged)
        {
            return $"{FormatBytes(file.OlderSizeBytes)} -> {FormatBytes(file.NewerSizeBytes)}";
        }

        if (modifiedChanged)
        {
            return $"timestamp updated {FormatUnixTimestamp(file.NewerLastModifiedUnix)}";
        }

        return "metadata shifted";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string FormatTriggerLabel(string trigger) =>
        trigger.Trim() switch
        {
            "Orchestration" => "Rescan orchestration",
            "Manual" => "Manual request",
            "" => "Manual request",
            _ => trigger
        };

    private static string FormatBuildModeLabel(string buildMode) =>
        buildMode.Trim() switch
        {
            "IncrementalComposition" => "Incremental composition",
            "FullRescan" => "Full rescan",
            "" => "Full rescan",
            _ => buildMode
        };

    private static string FormatDeltaSourceLabel(string deltaSource, string buildMode)
    {
        if (string.IsNullOrWhiteSpace(deltaSource))
        {
            return string.Equals(buildMode, "IncrementalComposition", StringComparison.OrdinalIgnoreCase)
                ? "Unspecified delta source"
                : "Direct scan path";
        }

        return deltaSource.Trim() switch
        {
            "UsnJournal" => "USN journal",
            "Watcher" => "Watcher",
            "ScheduledRescan" => "Scheduled rescan",
            _ => deltaSource
        };
    }

    private static string FormatTrustLabel(bool isTrusted) =>
        isTrusted ? "Trusted" : "Degraded";

    private static string FormatBaselineLabel(string baselineSessionId) =>
        string.IsNullOrWhiteSpace(baselineSessionId) ? "No baseline link" : $"Baseline {ShortId(baselineSessionId)}";

    private static string BuildStoredSessionProvenanceDetail(
        string trigger,
        string buildMode,
        string deltaSource,
        string baselineSessionId,
        bool isTrusted,
        string compositionNote)
    {
        var triggerLabel = FormatTriggerLabel(trigger);
        var buildModeLabel = FormatBuildModeLabel(buildMode);
        var deltaSourceLabel = FormatDeltaSourceLabel(deltaSource, buildMode);
        var baselineLabel = FormatBaselineLabel(baselineSessionId);
        var trustLabel = FormatTrustLabel(isTrusted);
        var note = string.IsNullOrWhiteSpace(compositionNote) ? string.Empty : $" Note: {compositionNote}";
        return $"{triggerLabel}, {buildModeLabel}, {deltaSourceLabel}, {baselineLabel}, {trustLabel}.{note}";
    }

    private static string FormatHistoryTimestamp(string timestamp)
    {
        return DateTimeOffset.TryParse(timestamp, out var parsed)
            ? parsed.LocalDateTime.ToString("g")
            : "Recently";
    }

    private static string FormatUnixTimestamp(long unixTimeSeconds)
    {
        if (unixTimeSeconds <= 0)
        {
            return "unknown time";
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds).LocalDateTime.ToString("g");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "unknown time";
        }
    }

    private static string ShortId(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value[..Math.Min(8, value.Length)];

    private static string FormatSignedDelta(int delta, string singular, string plural)
    {
        if (delta == 0)
        {
            return "Stable";
        }

        var noun = Math.Abs(delta) == 1 ? singular : plural;
        return delta > 0
            ? $"+{delta:N0} {noun}"
            : $"{delta:N0} {noun}";
    }

    private static string FormatBytes(long bytes)
    {
        var abs = Math.Abs(bytes);
        var prefix = bytes < 0 ? "-" : "";
        return abs switch
        {
            >= 1_073_741_824 => $"{prefix}{abs / 1_073_741_824.0:0.0} GB",
            >= 1_048_576 => $"{prefix}{abs / 1_048_576.0:0.0} MB",
            >= 1_024 => $"{prefix}{abs / 1_024.0:0.0} KB",
            _ => $"{prefix}{abs:N0} B"
        };
    }

    private static string FormatSignedBytes(long bytes)
    {
        if (bytes == 0) return "0 B";
        var prefix = bytes > 0 ? "+" : "";
        return $"{prefix}{FormatBytes(bytes)}";
    }

    private static string DescribeCadence(InventorySessionSummary latest, InventorySessionSummary previous)
    {
        if (!DateTimeOffset.TryParse(latest.CreatedUtc, out var latestTime)
            || !DateTimeOffset.TryParse(previous.CreatedUtc, out var previousTime))
        {
            return "Recent";
        }

        var gap = latestTime - previousTime;
        if (gap.TotalMinutes < 1)
        {
            return "Moments apart";
        }

        if (gap.TotalHours < 1)
        {
            return $"{Math.Round(gap.TotalMinutes):N0}m apart";
        }

        if (gap.TotalDays < 1)
        {
            return $"{Math.Round(gap.TotalHours):N0}h apart";
        }

        return $"{Math.Round(gap.TotalDays):N0}d apart";
    }

    private void SetConnectionMode(bool isLiveMode)
    {
        IsLiveMode = isLiveMode;
        if (isLiveMode)
        {
            ConnectionModeLabel = "Live service connected";
            ConnectionModeDetail = "Pipe calls are reaching the Atlas service, so scans, plans, and execution previews are using the privileged runtime.";
            RefreshInventorySignals();
            RefreshSignalCollections();
            RefreshHistoryMetrics();
            return;
        }

        ConnectionModeLabel = "Preview mode";
        ConnectionModeDetail = "The shell is falling back to local read-only heuristics. Execution remains blocked until the service is reachable.";
        RefreshInventorySignals();
        RefreshSignalCollections();
        RefreshHistoryMetrics();
    }

    private void ApplyScanState()
    {
        ReplaceAll(Volumes, currentScan.Volumes.Select(volume =>
        {
            var usedPercent = volume.TotalSizeBytes <= 0
                ? 0
                : (1d - ((double)volume.FreeSpaceBytes / volume.TotalSizeBytes)) * 100d;

            return new AtlasVolumeCard(
                $"{volume.RootPath} ({volume.DriveFormat})",
                $"{FormatBytes(volume.FreeSpaceBytes)} free",
                $"{usedPercent:0}% used on {volume.DriveType}");
        }));

        ReplaceAll(TopCategories, currentScan.Inventory
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Category) ? "Other" : item.Category)
            .OrderByDescending(group => group.Count())
            .Take(5)
            .Select(group =>
            {
                var sensitiveCount = group.Count(item => item.Sensitivity >= SensitivityLevel.High);
                var syncCount = group.Count(item => item.IsSyncManaged);
                return new AtlasCategoryCard(
                    group.Key,
                    $"{group.Count():N0} files",
                    sensitiveCount > 0
                        ? $"{sensitiveCount:N0} high-sensitivity items need extra care"
                        : syncCount > 0
                            ? $"{syncCount:N0} sync-managed items are being kept out by default"
                            : "Ready for category-level cleanup planning");
            }));

        ReplaceAll(DashboardMetrics,
        [
            new AtlasMetricCard("ACTIVE INVENTORY", $"{currentScan.Inventory.Count:N0}", "Files currently in the mutable workspace view."),
            new AtlasMetricCard("DUPLICATE SIGNAL", $"{currentScan.Duplicates.Count:N0}", "Duplicate groups conservative enough to surface in review."),
            new AtlasMetricCard("OPTIMIZATION PRESSURE", $"{currentOptimization.Findings.Count:N0}", "Safe fixes and recommendations discovered so far.")
        ]);

        RefreshInventorySignals();
        RefreshInventoryMemoryCards();
        RefreshScanContinuitySignals();
        RefreshRescanStoryCards();
        RefreshDriftReviewCards();
        RefreshScanPairSignals();
        RefreshDriftFileSampleCards();
        RefreshDriftHotspotCards();
        RefreshScanProvenanceSignals();

        ReplaceAll(CurrentStructureGroups, BuildCurrentStructureGroups());
        if (currentPlan.Plan.Operations.Count == 0)
        {
            ReplaceAll(ProposedStructureGroups, BuildProposedStructureGroups());
            PlanDiffNarrativeText = BuildIdleStructureNarrative();
        }

        RefreshHistoryMetrics();

        OnPropertyChanged(nameof(FilesScanned));
        OnPropertyChanged(nameof(DuplicateCount));
        OnPropertyChanged(nameof(CanDraftPlan));
    }

    private void ApplyPlanState()
    {
        var createCount = currentPlan.Plan.Operations.Count(operation => operation.Kind == OperationKind.CreateDirectory);
        var moveCount = currentPlan.Plan.Operations.Count(operation => operation.Kind is OperationKind.MovePath or OperationKind.RenamePath);
        var quarantineCount = currentPlan.Plan.Operations.Count(operation => operation.Kind == OperationKind.DeleteToQuarantine);

        ReplaceAll(PlanOperations, currentPlan.Plan.Operations.Select(operation =>
        {
            var pathLine = string.IsNullOrWhiteSpace(operation.DestinationPath)
                ? operation.SourcePath
                : $"{operation.SourcePath} -> {operation.DestinationPath}";

            var detail = $"{operation.Description} Confidence {operation.Confidence:P0}. Sensitivity {operation.Sensitivity}.";
            var needsReview = operation.Kind == OperationKind.DeleteToQuarantine
                || operation.Sensitivity >= SensitivityLevel.High
                || currentPlan.Plan.RequiresReview;

            return new AtlasPlanOperationCard(
                operation.Kind.ToString(),
                pathLine,
                detail,
                needsReview ? "Review gate" : "Ready",
                needsReview);
        }));

        var reviewCount = currentPlan.Plan.Operations.Count(operation =>
            operation.Kind == OperationKind.DeleteToQuarantine || operation.Sensitivity >= SensitivityLevel.High);

        PlanSummary = string.IsNullOrWhiteSpace(currentPlan.Summary)
            ? "A structured plan is ready for review."
            : currentPlan.Summary;

        RiskSummary = currentPlan.Plan.RiskSummary.BlockedReasons.Count > 0
            ? $"{currentPlan.Plan.RiskSummary.BlockedReasons.Count:N0} blocked reasons surfaced. Reversibility score {currentPlan.Plan.RiskSummary.ReversibilityScore:P0}."
            : $"{reviewCount:N0} review gates, confidence {currentPlan.Plan.RiskSummary.Confidence:P0}, reversibility {currentPlan.Plan.RiskSummary.ReversibilityScore:P0}.";

        ReplaceAll(PlanMetrics,
        [
            new AtlasMetricCard("TOTAL OPS", $"{currentPlan.Plan.Operations.Count:N0}", "Validated operations currently staged in the plan."),
            new AtlasMetricCard("DIRECTORY SETUP", $"{createCount:N0}", "New destination folders Atlas wants in place first."),
            new AtlasMetricCard("MOVE + RENAME", $"{moveCount:N0}", "Structural changes that reshape the user-visible tree."),
            new AtlasMetricCard("QUARANTINE", $"{quarantineCount:N0}", "Duplicate or cleanup actions routed through reversible quarantine.")
        ]);

        ReplaceAll(CurrentStructureGroups, BuildCurrentStructureGroups());
        ReplaceAll(ProposedStructureGroups, BuildProposedStructureGroups());
        PlanDiffNarrativeText = BuildPlanDiffNarrative(createCount, moveCount, quarantineCount, reviewCount);
        RefreshPlanSignals();
        RefreshHistoryMetrics();

        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(CanPreviewExecution));
        OnPropertyChanged(nameof(CanExecutePlan));
        OnPropertyChanged(nameof(PlanRationaleText));
        OnPropertyChanged(nameof(PlanBenefitText));
        OnPropertyChanged(nameof(PlanRollbackText));
    }

    private void ApplyOptimizationState()
    {
        var autoFixCount = currentOptimization.Findings.Count(finding => finding.CanAutoFix);
        var approvalCount = currentOptimization.Findings.Count(finding => finding.RequiresApproval);
        var manualCount = currentOptimization.Findings.Count - autoFixCount;

        ReplaceAll(OptimizationFindings, currentOptimization.Findings.Select(finding =>
        {
            var disposition = finding.CanAutoFix
                ? (finding.RequiresApproval ? "Auto-fix after approval" : "Auto-fix ready")
                : "Recommendation only";

            return new AtlasOptimizationCard(
                finding.Kind.ToString(),
                finding.Target,
                finding.Evidence,
                disposition,
                finding.RollbackPlan);
        }));

        OptimizationSummary = $"{autoFixCount:N0} safe-fix opportunities, {manualCount:N0} recommendation-only findings.";

        ReplaceAll(OptimizationMetrics,
        [
            new AtlasMetricCard("AUTO-FIX READY", $"{autoFixCount:N0}", "Curated low-risk fixes inside Atlas' safe optimization envelope."),
            new AtlasMetricCard("APPROVAL GATES", $"{approvalCount:N0}", "Findings that still need explicit review before Atlas touches them."),
            new AtlasMetricCard("RECOMMENDATION ONLY", $"{manualCount:N0}", "Signals Atlas will explain but not auto-apply.")
        ]);

        OnPropertyChanged(nameof(HasOptimizationFindings));
        ApplyScanState();
    }

    private void ApplyUndoState()
    {
        if (latestCheckpoint.CheckpointId == string.Empty)
        {
            latestCheckpoint = new UndoCheckpoint();
        }

        UndoSummary = HasUndoCheckpoint
            ? $"{latestCheckpoint.InverseOperations.Count:N0} inverse operations and {latestCheckpoint.QuarantineItems.Count:N0} quarantined items are available."
            : persistedHistory.RecentCheckpoints.Count > 0
                ? $"{persistedHistory.RecentCheckpoints.Count:N0} stored checkpoints are available from prior service runs."
            : "No rollback metadata has been produced in this session yet.";

        ReplaceAll(UndoMetrics,
        [
            new AtlasMetricCard("INVERSE OPS", $"{latestCheckpoint.InverseOperations.Count:N0}", "Concrete steps Atlas can replay to reverse the latest batch."),
            new AtlasMetricCard("QUARANTINE ITEMS", $"{latestCheckpoint.QuarantineItems.Count:N0}", "Files still restorable without a full batch restore."),
            new AtlasMetricCard("CHECKPOINT NOTES", $"{latestCheckpoint.Notes.Count:N0}", "Recovery annotations staged alongside the latest undo story.")
        ]);

        RebuildRecoveryCollections();
        RefreshUndoSignals();
        RefreshHistoryMetrics();

        OnPropertyChanged(nameof(HasUndoCheckpoint));
        OnPropertyChanged(nameof(CanPreviewUndo));
        OnPropertyChanged(nameof(CanExecuteUndo));
        OnPropertyChanged(nameof(UndoNotesText));
    }

    private void RefreshSignalCollections()
    {
        RefreshInventorySignals();
        RefreshInventoryMemoryCards();
        RefreshScanContinuitySignals();
        RefreshRescanStoryCards();
        RefreshDriftReviewCards();
        RefreshScanPairSignals();
        RefreshDriftFileSampleCards();
        RefreshDriftHotspotCards();
        RefreshScanProvenanceSignals();

        if (currentPlan.Plan.Operations.Count > 0)
        {
            RefreshPlanSignals();
        }

        if (HasUndoCheckpoint)
        {
            RefreshUndoSignals();
        }
    }

    private void RefreshInventorySignals()
    {
        var rootValue = MutableRoots.Count == 0 ? "Unscoped" : $"{MutableRoots.Count:N0} mutable roots";
        var rootDetail = MutableRoots.Count == 0
            ? "Atlas needs at least one approved mutable root before it can build a trustworthy organization inventory."
            : BuildCollectionSummary(MutableRoots, "root");

        var syncValue = profile.ExcludeSyncFoldersByDefault ? "Excluded" : "Included with care";
        var syncDetail = profile.ExcludeSyncFoldersByDefault
            ? "Sync-managed folders stay out by default so Atlas does not collide with cloud reconciliation or user expectations."
            : "Sync-managed folders are allowed into scope, so destructive moves need extra review discipline.";

        var duplicateValue = currentScan.Duplicates.Count switch
        {
            0 => "Low",
            <= 5 => "Watched",
            <= 20 => "Elevated",
            _ => "Heavy"
        };
        var duplicateDetail = currentScan.Duplicates.Count == 0
            ? "No duplicate groups are currently surfacing in the live or preview inventory."
            : $"{currentScan.Duplicates.Count:N0} conservative duplicate groups are visible for review in the latest scan.";

        var constrainedVolumes = currentScan.Volumes.Count(volume =>
            volume.TotalSizeBytes > 0
            && ((double)volume.FreeSpaceBytes / volume.TotalSizeBytes) <= 0.15d);
        var volumeValue = currentScan.Volumes.Count == 0
            ? "Awaiting scan"
            : constrainedVolumes == 0
                ? $"{currentScan.Volumes.Count:N0} healthy"
                : $"{constrainedVolumes:N0} constrained";
        var volumeDetail = currentScan.Volumes.Count == 0
            ? "Run a scan to capture mounted volume posture and free-space pressure."
            : constrainedVolumes == 0
                ? $"Atlas sees {currentScan.Volumes.Count:N0} mounted volumes and none are currently under the low-free-space threshold."
                : $"{constrainedVolumes:N0} mounted volumes are under roughly 15% free space and may need extra cleanup attention.";

        var storedValue = !persistedInventorySnapshot.HasSession
            ? (IsLiveMode ? "No stored scans yet" : "Service offline")
            : $"{persistedInventorySessions.Sessions.Count:N0} sessions";
        var storedDetail = !persistedInventorySnapshot.HasSession
            ? (IsLiveMode
                ? "The service is reachable, but Atlas has not retained any scan sessions yet."
                : "Stored scan history becomes available when the service can answer inventory queries.")
            : $"Latest stored session captured {persistedInventorySnapshot.FilesScanned:N0} files across {persistedInventorySnapshot.RootCount:N0} roots and {persistedInventorySnapshot.VolumeCount:N0} volumes on {FormatHistoryTimestamp(persistedInventorySnapshot.CreatedUtc)}.";

        ReplaceAll(InventorySignals,
        [
            new AtlasSignalCard("ROOT SCOPE", rootValue, rootDetail),
            new AtlasSignalCard("SYNC POLICY", syncValue, syncDetail),
            new AtlasSignalCard("DUPLICATE PRESSURE", duplicateValue, duplicateDetail),
            new AtlasSignalCard("VOLUME HEALTH", volumeValue, volumeDetail),
            new AtlasSignalCard("STORED SCANS", storedValue, storedDetail)
        ]);
    }

    private void RefreshInventoryMemoryCards()
    {
        var cards = new List<AtlasStoredMemoryCard>();

        if (currentScan.Inventory.Count == 0 && !persistedInventorySnapshot.HasSession)
        {
            cards.Add(new AtlasStoredMemoryCard(
                "AWAITING INVENTORY",
                "No scan session is loaded yet",
                "Run a scan to populate Atlas with a fresh inventory view before planning or cleanup work starts."));

            InventoryMemorySummaryText = "The dashboard is still waiting for its first inventory session in this shell.";
            ReplaceAll(InventoryMemoryCards, cards);
            return;
        }

        cards.Add(new AtlasStoredMemoryCard(
            "CURRENT SESSION",
            $"{currentScan.Inventory.Count:N0} files across {MutableRoots.Count:N0} mutable roots",
            IsLiveMode
                ? "This scan came through the live service path, so the current inventory can grow into persisted scan memory."
                : "This scan is preview-shaped locally while the service remains offline."));

        var leadingCategory = TopCategories.FirstOrDefault();
        cards.Add(new AtlasStoredMemoryCard(
            "CATEGORY SHAPE",
            leadingCategory?.Title ?? "Mixed inventory",
            leadingCategory is null
                ? "Atlas will summarize the dominant categories after the next scan."
                : $"{leadingCategory.Value}. {leadingCategory.Detail}"));

        var mostConstrainedVolume = currentScan.Volumes
            .Where(volume => volume.TotalSizeBytes > 0)
            .OrderBy(volume => (double)volume.FreeSpaceBytes / volume.TotalSizeBytes)
            .FirstOrDefault();

        cards.Add(new AtlasStoredMemoryCard(
            "VOLUME PRESSURE",
            mostConstrainedVolume is null
                ? "No mounted volume data"
                : mostConstrainedVolume.RootPath,
            mostConstrainedVolume is null
                ? "Atlas will add mounted-volume memory after the next scan."
                : $"{FormatBytes(mostConstrainedVolume.FreeSpaceBytes)} free of {FormatBytes(mostConstrainedVolume.TotalSizeBytes)} on {mostConstrainedVolume.DriveType}."));

        cards.Add(new AtlasStoredMemoryCard(
            "DUPLICATE REVIEW",
            currentScan.Duplicates.Count == 0
                ? "No duplicate groups"
                : $"{currentScan.Duplicates.Count:N0} duplicate groups",
            currentScan.Duplicates.Count == 0
                ? "Atlas has not surfaced conservative duplicate candidates in the current inventory."
                : "Duplicate groups are available for quarantine-first review in the planning lane."));

        if (persistedInventorySnapshot.HasSession)
        {
            var storedRoots = persistedInventoryDetail.Roots.Count > 0
                ? BuildCollectionSummary(persistedInventoryDetail.Roots, "stored root")
                : $"{persistedInventorySnapshot.RootCount:N0} stored roots are available for drill-in.";

            cards.Add(new AtlasStoredMemoryCard(
                "STORED SESSION",
                $"{persistedInventorySnapshot.FilesScanned:N0} files on {FormatHistoryTimestamp(persistedInventorySnapshot.CreatedUtc)}",
                $"{persistedInventorySnapshot.DuplicateGroupCount:N0} duplicate groups, {persistedInventorySnapshot.RootCount:N0} roots, and {persistedInventorySnapshot.VolumeCount:N0} volumes are retained by the service."));

            cards.Add(new AtlasStoredMemoryCard(
                "STORED ROOTS",
                persistedInventoryDetail.Roots.Count == 0
                    ? $"{persistedInventorySnapshot.RootCount:N0} roots captured"
                    : $"{persistedInventoryDetail.Roots.Count:N0} roots captured",
                storedRoots));

            cards.Add(new AtlasStoredMemoryCard(
                "STORED FILE SHAPE",
                persistedInventoryFiles.TotalCount == 0
                    ? "Awaiting stored file rows"
                    : $"{persistedInventoryFiles.TotalCount:N0} stored file rows",
                BuildStoredFileSampleDetail(persistedInventoryFiles.Files)));
        }

        InventoryMemorySummaryText = persistedInventorySnapshot.HasSession
            ? $"{currentScan.Inventory.Count:N0} files are active in the current shell session, while the latest stored scan retained {persistedInventorySnapshot.FilesScanned:N0} files and {persistedInventorySessions.Sessions.Count:N0} recent sessions for later review."
            : $"{currentScan.Inventory.Count:N0} files, {currentScan.Volumes.Count:N0} mounted volumes, and {currentScan.Duplicates.Count:N0} duplicate groups define the current scan session.";
        ReplaceAll(InventoryMemoryCards, cards);
    }

    private void RefreshScanContinuitySignals()
    {
        if (persistedInventorySessions.Sessions.Count == 0)
        {
            ReplaceAll(ScanContinuitySignals,
            [
                new AtlasSignalCard(
                    "SCAN CADENCE",
                    IsLiveMode ? "Awaiting baseline" : "Service offline",
                    IsLiveMode
                        ? "Atlas needs at least one persisted session before it can describe continuity."
                        : "Scan continuity becomes available when the service can answer inventory queries.")
            ]);

            ScanContinuitySummaryText = "Scan continuity will appear here once Atlas can compare persisted sessions.";
            return;
        }

        var latest = persistedInventorySessions.Sessions[0];
        var previous = persistedInventorySessions.Sessions.Count > 1 ? persistedInventorySessions.Sessions[1] : null;

        var cadenceValue = previous is null
            ? "Baseline only"
            : DescribeCadence(latest, previous);
        var cadenceDetail = previous is null
            ? $"Atlas has one stored scan from {FormatHistoryTimestamp(latest.CreatedUtc)}. A second persisted scan will unlock trend comparisons."
            : $"Latest stored scan landed {FormatHistoryTimestamp(latest.CreatedUtc)} after the prior snapshot on {FormatHistoryTimestamp(previous.CreatedUtc)}.";

        var hasDriftBaseline = previous is not null
            && persistedDriftSnapshot.HasBaseline
            && string.Equals(persistedDriftSnapshot.OlderSessionId, previous.SessionId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(persistedDriftSnapshot.NewerSessionId, latest.SessionId, StringComparison.OrdinalIgnoreCase);
        var fileDelta = previous is null ? 0 : latest.FilesScanned - previous.FilesScanned;
        var driftPathCount = hasDriftBaseline
            ? persistedDriftSnapshot.AddedCount + persistedDriftSnapshot.RemovedCount + persistedDriftSnapshot.ChangedCount
            : Math.Abs(fileDelta);
        var fileTrendValue = previous is null
            ? $"{latest.FilesScanned:N0} files"
            : hasDriftBaseline
                ? $"{driftPathCount:N0} changed paths"
                : FormatSignedDelta(fileDelta, "file", "files");
        var fileTrendDetail = previous is null
            ? "This is the first persisted inventory baseline Atlas can compare against later."
            : hasDriftBaseline
                ? $"{persistedDriftSnapshot.AddedCount:N0} added, {persistedDriftSnapshot.RemovedCount:N0} removed, and {persistedDriftSnapshot.ChangedCount:N0} changed paths between the two most recent stored scans."
                : fileDelta == 0
                    ? "Stored file count is unchanged between the two most recent persisted scans."
                    : $"{latest.FilesScanned:N0} files in the latest scan versus {previous.FilesScanned:N0} in the previous one.";

        var duplicateDelta = previous is null ? 0 : latest.DuplicateGroupCount - previous.DuplicateGroupCount;
        var duplicateValue = previous is null ? $"{latest.DuplicateGroupCount:N0} groups" : FormatSignedDelta(duplicateDelta, "group", "groups");
        var duplicateDetail = previous is null
            ? "Duplicate pressure will become directional once another persisted scan is available."
            : duplicateDelta == 0
                ? "Duplicate-group pressure is flat across the two most recent stored scans."
                : $"{latest.DuplicateGroupCount:N0} duplicate groups now versus {previous.DuplicateGroupCount:N0} in the prior persisted scan.";

        var scopeChanged = previous is not null
            && (latest.RootCount != previous.RootCount || latest.VolumeCount != previous.VolumeCount);
        var scopeValue = previous is null
            ? $"{latest.RootCount:N0} roots / {latest.VolumeCount:N0} volumes"
            : scopeChanged
                ? "Scope changed"
                : "Stable";
        var scopeDetail = previous is null
            ? "Stored scope shape will be compared once Atlas has more than one persisted scan."
            : scopeChanged
                ? $"Roots moved from {previous.RootCount:N0} to {latest.RootCount:N0} and volumes from {previous.VolumeCount:N0} to {latest.VolumeCount:N0}."
                : "Mutable-root and volume counts stayed stable across the last two persisted scans.";

        ReplaceAll(ScanContinuitySignals,
        [
            new AtlasSignalCard("SCAN CADENCE", cadenceValue, cadenceDetail),
            new AtlasSignalCard("FILE TREND", fileTrendValue, fileTrendDetail),
            new AtlasSignalCard("DUPLICATE TREND", duplicateValue, duplicateDetail),
            new AtlasSignalCard("SCOPE STABILITY", scopeValue, scopeDetail)
        ]);

        ScanContinuitySummaryText = previous is null
            ? $"Atlas has one stored scan baseline from {FormatHistoryTimestamp(latest.CreatedUtc)} and is waiting for the next persisted session to establish drift."
            : hasDriftBaseline
                 ? $"{cadenceValue}. The latest stored pair shows {persistedDriftSnapshot.AddedCount:N0} added, {persistedDriftSnapshot.RemovedCount:N0} removed, and {persistedDriftSnapshot.ChangedCount:N0} changed paths; duplicate trend is {duplicateValue.ToLowerInvariant()} and scope is {(scopeChanged ? "changing" : "stable")}."
                 : $"{cadenceValue}. File trend {fileTrendValue}; duplicate trend {duplicateValue}; scope is {(scopeChanged ? "changing" : "stable")} across the last two stored scans.";
    }

    private void RefreshRescanStoryCards()
    {
        if (persistedInventorySessions.Sessions.Count == 0)
        {
            ReplaceAll(RescanStoryCards,
            [
                new AtlasStoredMemoryCard(
                    "AWAITING STORY",
                    "No stored rescan lineage yet",
                    IsLiveMode
                        ? "Atlas needs persisted sessions before it can tell a reliable rescan story."
                        : "The rescan story becomes available when the service can return stored session history.")
            ]);

            RescanStorySummaryText = "Rescan story will appear here once Atlas can compare stored sessions.";
            return;
        }

        var latest = persistedInventorySessions.Sessions[0];
        var previous = persistedInventorySessions.Sessions.Count > 1 ? persistedInventorySessions.Sessions[1] : null;
        var oldest = persistedInventorySessions.Sessions[^1];

        if (previous is null)
        {
            var baselineTrigger = FormatTriggerLabel(latest.Trigger);
            var baselineBuildMode = FormatBuildModeLabel(latest.BuildMode);
            var baselineDeltaSource = FormatDeltaSourceLabel(latest.DeltaSource, latest.BuildMode);
            var baselineTrust = FormatTrustLabel(latest.IsTrusted);
            var baselineLink = FormatBaselineLabel(latest.BaselineSessionId);
            var baselineNote = string.IsNullOrWhiteSpace(latest.CompositionNote) ? "No composition note was retained." : latest.CompositionNote;

            ReplaceAll(RescanStoryCards,
            [
                new AtlasStoredMemoryCard(
                    "BASELINE STORY",
                    $"{latest.FilesScanned:N0} files captured on {FormatHistoryTimestamp(latest.CreatedUtc)}",
                    "Atlas has one persisted scan baseline. The next stored session will turn this into a real rescan storyline."),
                new AtlasStoredMemoryCard(
                    "LATEST MODE",
                    $"{baselineBuildMode} / {baselineTrust}",
                    $"{baselineTrigger} using {baselineDeltaSource}. {baselineLink}. {baselineNote}"),
                new AtlasStoredMemoryCard(
                    "SESSION DEPTH",
                    "1 stored session",
                    $"{latest.RootCount:N0} roots and {latest.VolumeCount:N0} volumes are retained from the first persisted capture."),
                new AtlasStoredMemoryCard(
                    "EVIDENCE MODEL",
                    "Baseline only",
                    "Atlas can describe the first stored snapshot, but it still needs a second session before it can narrate change.")
            ]);

            RescanStorySummaryText = $"Atlas has one stored scan baseline from {FormatHistoryTimestamp(latest.CreatedUtc)} and is waiting for the next persisted session to establish a rescan story.";
            return;
        }

        var hasDriftBaseline = persistedDriftSnapshot.HasBaseline
            && string.Equals(persistedDriftSnapshot.OlderSessionId, previous.SessionId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(persistedDriftSnapshot.NewerSessionId, latest.SessionId, StringComparison.OrdinalIgnoreCase);
        var driftCount = persistedDriftSnapshot.AddedCount + persistedDriftSnapshot.RemovedCount + persistedDriftSnapshot.ChangedCount;
        var scopeChanged = latest.RootCount != previous.RootCount || latest.VolumeCount != previous.VolumeCount;
        var latestTrigger = FormatTriggerLabel(latest.Trigger);
        var latestBuildMode = FormatBuildModeLabel(latest.BuildMode);
        var latestDeltaSource = FormatDeltaSourceLabel(latest.DeltaSource, latest.BuildMode);
        var latestTrust = FormatTrustLabel(latest.IsTrusted);
        var latestBaseline = FormatBaselineLabel(latest.BaselineSessionId);
        var latestNote = string.IsNullOrWhiteSpace(latest.CompositionNote) ? "No composition note was retained." : latest.CompositionNote;
        var evidenceTitle = persistedDiffFiles.Files.Count > 0
            ? $"{latestBuildMode} / {latestTrust}"
            : hasDriftBaseline
                ? $"{latestBuildMode} / {latestTrust}"
                : "Session pair only";
        var evidenceDetail = persistedDiffFiles.Files.Count > 0
            ? $"{latestTrigger} using {latestDeltaSource}. {latestBaseline}. {latestNote}"
            : hasDriftBaseline
                ? $"Atlas can prove added, removed, and changed counts for the newest stored pair. Latest session came from {latestTrigger.ToLowerInvariant()} using {latestDeltaSource.ToLowerInvariant()}. {latestBaseline}. {latestNote}"
                : "Atlas has the latest stored pair, but exact drift evidence still needs to be loaded through the diff routes.";
        var movementTitle = hasDriftBaseline
            ? $"{persistedDriftSnapshot.AddedCount:N0} add / {persistedDriftSnapshot.RemovedCount:N0} remove / {persistedDriftSnapshot.ChangedCount:N0} change"
            : FormatSignedDelta(latest.FilesScanned - previous.FilesScanned, "file", "files");
        var movementDetail = hasDriftBaseline
            ? $"Across {DescribeCadence(latest, previous)}, Atlas tracked {driftCount:N0} changed paths between the newest two stored sessions. Latest capture used {latestBuildMode.ToLowerInvariant()}."
            : $"{latest.FilesScanned:N0} files in the newest stored session versus {previous.FilesScanned:N0} in the prior one. Latest capture used {latestBuildMode.ToLowerInvariant()}.";
        var depthDetail = string.Equals(oldest.SessionId, latest.SessionId, StringComparison.OrdinalIgnoreCase)
            ? "Atlas is still working from a single retained baseline."
            : $"This memory window spans {FormatHistoryTimestamp(oldest.CreatedUtc)} through {FormatHistoryTimestamp(latest.CreatedUtc)}.";
        var scopeTitle = scopeChanged ? "Scope shifted" : "Scope held steady";
        var scopeDetail = scopeChanged
            ? $"Roots moved from {previous.RootCount:N0} to {latest.RootCount:N0} and volumes from {previous.VolumeCount:N0} to {latest.VolumeCount:N0} across the newest pair."
            : $"Roots stayed at {latest.RootCount:N0} and volumes stayed at {latest.VolumeCount:N0} across the newest pair.";

        ReplaceAll(RescanStoryCards,
        [
            new AtlasStoredMemoryCard(
                "LATEST WINDOW",
                $"{FormatHistoryTimestamp(previous.CreatedUtc)} -> {FormatHistoryTimestamp(latest.CreatedUtc)}",
                movementDetail),
            new AtlasStoredMemoryCard(
                "SESSION DEPTH",
                $"{persistedInventorySessions.Sessions.Count:N0} stored sessions",
                depthDetail),
            new AtlasStoredMemoryCard(
                "EVIDENCE MODEL",
                evidenceTitle,
                evidenceDetail),
            new AtlasStoredMemoryCard(
                "SCOPE POSTURE",
                scopeTitle,
                scopeDetail)
        ]);

        RescanStorySummaryText = hasDriftBaseline
            ? $"Atlas can narrate the newest stored rescan window from {FormatHistoryTimestamp(previous.CreatedUtc)} to {FormatHistoryTimestamp(latest.CreatedUtc)} with {movementTitle.ToLowerInvariant()}, {evidenceTitle.ToLowerInvariant()}, and {(scopeChanged ? "shifting" : "stable")} scope."
            : $"Atlas can narrate stored session depth and the newest comparison window, but detailed drift evidence still needs to be loaded for the latest pair.";
    }

    private void RefreshDriftReviewCards()
    {
        if (persistedInventorySessions.Sessions.Count == 0)
        {
            ReplaceAll(DriftReviewCards,
            [
                new AtlasStoredMemoryCard(
                    "WAITING FOR DRIFT",
                    "No stored scan pair yet",
                    IsLiveMode
                        ? "Atlas needs persisted scan sessions before it can review what changed between them."
                        : "Stored drift review becomes available when the service can answer inventory history queries.")
            ]);

            DriftReviewSummaryText = "Drift review will appear here once Atlas can compare stored scan sessions more explicitly.";
            return;
        }

        var latest = persistedInventorySessions.Sessions[0];
        var previous = persistedInventorySessions.Sessions.Count > 1 ? persistedInventorySessions.Sessions[1] : null;
        if (previous is null)
        {
            ReplaceAll(DriftReviewCards,
            [
                new AtlasStoredMemoryCard(
                    "BASELINE READY",
                    $"{latest.FilesScanned:N0} files in the first stored scan",
                    $"Atlas captured a baseline on {FormatHistoryTimestamp(latest.CreatedUtc)}. The next persisted session will turn this lane into a true drift review surface."),
                new AtlasStoredMemoryCard(
                    "NEXT UPGRADE",
                    "Awaiting explicit diff evidence",
                    "The shell is ready to show added, removed, and changed file rows as soon as the service exposes drift routes.")
            ]);

            DriftReviewSummaryText = $"Atlas has one stored scan baseline from {FormatHistoryTimestamp(latest.CreatedUtc)} and is waiting for a second session before it can compare drift.";
            return;
        }

        var hasDriftBaseline = persistedDriftSnapshot.HasBaseline
            && string.Equals(persistedDriftSnapshot.OlderSessionId, previous.SessionId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(persistedDriftSnapshot.NewerSessionId, latest.SessionId, StringComparison.OrdinalIgnoreCase);

        if (!hasDriftBaseline)
        {
            ReplaceAll(DriftReviewCards,
            [
                new AtlasStoredMemoryCard(
                    "LATEST PAIR",
                    $"{FormatHistoryTimestamp(previous.CreatedUtc)} -> {FormatHistoryTimestamp(latest.CreatedUtc)}",
                    "Atlas has the right stored scan pair, but the exact drift payload is not available yet."),
                new AtlasStoredMemoryCard(
                    "DRIFT ROUTE",
                    IsLiveMode ? "Waiting on diff evidence" : "Service offline",
                    IsLiveMode
                        ? "The shell is live, but it still needs the exact diff snapshot to show added, removed, and changed paths."
                        : "Preview mode keeps the page shape warm, but exact drift evidence arrives only through the service.")
            ]);

            DriftReviewSummaryText = "Atlas can see the latest stored scan pair, but detailed drift evidence is not available yet.";
            return;
        }

        var diff = persistedSessionDiff.Found
            ? persistedSessionDiff
            : new SessionDiffResponse
            {
                Found = true,
                OlderSessionId = persistedDriftSnapshot.OlderSessionId,
                NewerSessionId = persistedDriftSnapshot.NewerSessionId,
                AddedCount = persistedDriftSnapshot.AddedCount,
                RemovedCount = persistedDriftSnapshot.RemovedCount,
                ChangedCount = persistedDriftSnapshot.ChangedCount,
                UnchangedCount = persistedDriftSnapshot.UnchangedCount
            };
        var diffFiles = persistedDiffFiles.Found
            ? persistedDiffFiles.Files
            : new List<DiffFileSummary>();
        var cadence = DescribeCadence(latest, previous);

        var addedBytes = diffFiles.Where(f => f.ChangeKind == "Added").Sum(f => f.NewerSizeBytes);
        var removedBytes = diffFiles.Where(f => f.ChangeKind == "Removed").Sum(f => f.OlderSizeBytes);
        var changedDelta = diffFiles.Where(f => f.ChangeKind == "Changed").Sum(f => f.NewerSizeBytes - f.OlderSizeBytes);
        var netBytes = addedBytes - removedBytes + changedDelta;
        var totalChanged = diff.AddedCount + diff.RemovedCount + diff.ChangedCount;
        var sizeIsPartial = diffFiles.Count < totalChanged;
        var netSizeTitle = netBytes == 0 && diffFiles.Count == 0
            ? "Awaiting sample"
            : netBytes == 0
                ? "Stable"
                : $"{FormatSignedBytes(netBytes)} net";
        var netSizeDetail = diffFiles.Count == 0
            ? "Size impact will appear once Atlas has bounded diff file rows to measure."
            : $"{FormatSignedBytes(addedBytes)} added, {FormatSignedBytes(-removedBytes)} removed, {FormatSignedBytes(changedDelta)} changed delta{(sizeIsPartial ? " from bounded sample" : "")}.";

        var topExtensions = diffFiles
            .Select(f => Path.GetExtension(f.Path))
            .Where(ext => !string.IsNullOrWhiteSpace(ext))
            .GroupBy(ext => ext.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Take(3)
            .ToList();
        var extTitle = topExtensions.Count == 0
            ? "No pattern"
            : topExtensions.Count == 1 || topExtensions[0].Count() > topExtensions.Skip(1).Sum(g => g.Count())
                ? topExtensions[0].Key
                : "Mixed";
        var extDetail = topExtensions.Count == 0
            ? "No extension pattern emerged from the current bounded sample."
            : string.Join(", ", topExtensions.Select(g => $"{g.Key} ({g.Count()})"));

        ReplaceAll(DriftReviewCards,
        [
            new AtlasStoredMemoryCard(
                "LATEST PAIR",
                $"{FormatHistoryTimestamp(previous.CreatedUtc)} -> {FormatHistoryTimestamp(latest.CreatedUtc)}",
                $"Cadence is {cadence}. Atlas is comparing the stored sessions from {ShortId(previous.SessionId)} and {ShortId(latest.SessionId)}."),
            CreateDiffEvidenceCard(
                "ADDED PATHS",
                diff.AddedCount,
                diffFiles,
                "Added",
                "No added paths",
                "No new paths appeared between the last two stored scans."),
            CreateDiffEvidenceCard(
                "REMOVED PATHS",
                diff.RemovedCount,
                diffFiles,
                "Removed",
                "No removed paths",
                "No paths disappeared between the last two stored scans."),
            CreateDiffEvidenceCard(
                "CHANGED PATHS",
                diff.ChangedCount,
                diffFiles,
                "Changed",
                "No changed paths",
                "No path-stable files changed size or modified time between the last two stored scans."),
            new AtlasStoredMemoryCard(
                "PATH IDENTITY",
                $"{diff.UnchangedCount:N0} unchanged paths",
                "Diff stays path-stable for safety. A move or rename appears as one removed path plus one added path until rename tracking is introduced."),
            new AtlasStoredMemoryCard(
                "NET SIZE IMPACT",
                netSizeTitle,
                netSizeDetail),
            new AtlasStoredMemoryCard(
                "EXTENSION SIGNAL",
                extTitle,
                extDetail)
        ]);

        var totalChangedPaths = diff.AddedCount + diff.RemovedCount + diff.ChangedCount;
        DriftReviewSummaryText = totalChangedPaths == 0
            ? $"The last two stored scans are path-stable. Atlas found {diff.UnchangedCount:N0} unchanged paths and no added, removed, or changed rows in the current drift window."
            : $"Across the latest stored scan pair, Atlas found {diff.AddedCount:N0} added, {diff.RemovedCount:N0} removed, and {diff.ChangedCount:N0} changed paths. The cards below show bounded file evidence from that drift window.";
    }

    private void RefreshScanPairSignals()
    {
        if (persistedInventorySessions.Sessions.Count == 0)
        {
            ReplaceAll(ScanPairSignals,
            [
                new AtlasSignalCard(
                    "SESSION PAIR",
                    IsLiveMode ? "Awaiting baseline" : "Service offline",
                    IsLiveMode
                        ? "Atlas needs two persisted scans before it can describe a session pair."
                        : "Session-pair intelligence appears when the service can answer stored scan queries.")
            ]);

            ScanPairSummaryText = "Stored session-pair intelligence will appear here once Atlas can compare two persisted scans.";
            return;
        }

        var latest = persistedInventorySessions.Sessions[0];
        var previous = persistedInventorySessions.Sessions.Count > 1 ? persistedInventorySessions.Sessions[1] : null;
        if (previous is null)
        {
            ReplaceAll(ScanPairSignals,
            [
                new AtlasSignalCard(
                    "SESSION PAIR",
                    "Baseline only",
                    $"Atlas has one stored scan from {FormatHistoryTimestamp(latest.CreatedUtc)} and is waiting for the next one to establish a comparison window."),
                new AtlasSignalCard(
                    "DIFF MODEL",
                    "Path-stable",
                    "Once a second session lands, Atlas will compare paths conservatively. Moves currently surface as one removed path plus one added path.")
            ]);

            ScanPairSummaryText = $"Atlas has one stored scan baseline from {FormatHistoryTimestamp(latest.CreatedUtc)} and is waiting for a second persisted session before it can describe a real window.";
            return;
        }

        var hasDriftBaseline = persistedDriftSnapshot.HasBaseline
            && string.Equals(persistedDriftSnapshot.OlderSessionId, previous.SessionId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(persistedDriftSnapshot.NewerSessionId, latest.SessionId, StringComparison.OrdinalIgnoreCase);

        if (!hasDriftBaseline)
        {
            ReplaceAll(ScanPairSignals,
            [
                new AtlasSignalCard(
                    "SESSION PAIR",
                    $"{FormatHistoryTimestamp(previous.CreatedUtc)} -> {FormatHistoryTimestamp(latest.CreatedUtc)}",
                    "Atlas has a stored pair, but the exact diff snapshot is not available yet."),
                new AtlasSignalCard(
                    "DIFF ROUTE",
                    IsLiveMode ? "Pending" : "Service offline",
                    IsLiveMode
                        ? "The session pair is valid, but Atlas still needs the service diff response to describe the window."
                        : "Preview mode cannot surface the stored diff response.")
            ]);

            ScanPairSummaryText = "Atlas can see the latest stored session pair, but the current diff window is not available yet.";
            return;
        }

        var diff = persistedSessionDiff.Found
            ? persistedSessionDiff
            : new SessionDiffResponse
            {
                Found = true,
                OlderSessionId = persistedDriftSnapshot.OlderSessionId,
                NewerSessionId = persistedDriftSnapshot.NewerSessionId,
                AddedCount = persistedDriftSnapshot.AddedCount,
                RemovedCount = persistedDriftSnapshot.RemovedCount,
                ChangedCount = persistedDriftSnapshot.ChangedCount,
                UnchangedCount = persistedDriftSnapshot.UnchangedCount
            };

        var totalChangedPaths = diff.AddedCount + diff.RemovedCount + diff.ChangedCount;
        var diffFiles = persistedDiffFiles.Found ? persistedDiffFiles.Files : new List<DiffFileSummary>();
        var addedBytes = diffFiles.Where(f => f.ChangeKind == "Added").Sum(f => f.NewerSizeBytes);
        var removedBytes = diffFiles.Where(f => f.ChangeKind == "Removed").Sum(f => f.OlderSizeBytes);
        var changedDelta = diffFiles.Where(f => f.ChangeKind == "Changed").Sum(f => f.NewerSizeBytes - f.OlderSizeBytes);
        var netBytes = addedBytes - removedBytes + changedDelta;
        var sizeIsPartial = diffFiles.Count < totalChangedPaths;
        var sizeValue = netBytes == 0 && diffFiles.Count == 0
            ? "Awaiting sample"
            : netBytes == 0
                ? "Stable"
                : $"{FormatSignedBytes(netBytes)}{(sizeIsPartial ? " (sample)" : "")}";
        var sizeDetail = diffFiles.Count == 0
            ? "Size impact will appear once Atlas has bounded diff file rows to measure."
            : $"{FormatSignedBytes(addedBytes)} added, {FormatSignedBytes(-removedBytes)} removed, {FormatSignedBytes(changedDelta)} changed delta{(sizeIsPartial ? " from bounded sample" : "")}.";

        var totalPaths = diff.AddedCount + diff.RemovedCount + diff.ChangedCount + diff.UnchangedCount;
        var churnPercent = totalPaths == 0 ? 0.0 : (double)totalChangedPaths / totalPaths * 100;
        var churnValue = totalChangedPaths == 0 ? "Stable" : $"{churnPercent:0.0}%";
        var churnLabel = churnPercent < 1 ? "minimal" : churnPercent < 10 ? "moderate" : "significant";
        var churnDetail = totalChangedPaths == 0
            ? "No paths changed between the two most recent stored scans."
            : $"{totalChangedPaths:N0} of {totalPaths:N0} total paths changed — {churnLabel} churn across the current drift window.";

        ReplaceAll(ScanPairSignals,
        [
            new AtlasSignalCard(
                "PAIR WINDOW",
                $"{FormatHistoryTimestamp(previous.CreatedUtc)} -> {FormatHistoryTimestamp(latest.CreatedUtc)}",
                $"Comparing stored sessions {ShortId(previous.SessionId)} and {ShortId(latest.SessionId)}."),
            new AtlasSignalCard(
                "CHANGED PATHS",
                $"{totalChangedPaths:N0}",
                $"{diff.AddedCount:N0} added, {diff.RemovedCount:N0} removed, and {diff.ChangedCount:N0} changed paths sit inside the current window."),
            new AtlasSignalCard(
                "UNCHANGED PATHS",
                $"{diff.UnchangedCount:N0}",
                "Paths that remained stable across the current pair."),
            new AtlasSignalCard(
                "SIZE IMPACT",
                sizeValue,
                sizeDetail),
            new AtlasSignalCard(
                "CHURN RATE",
                churnValue,
                churnDetail),
            new AtlasSignalCard(
                "DIFF MODEL",
                "Path-stable",
                "Moves and renames still surface as removed plus added until rename tracking is introduced.")
        ]);

        ScanPairSummaryText = totalChangedPaths == 0
            ? $"The latest stored session pair is stable. Atlas found {diff.UnchangedCount:N0} unchanged paths and no current drift."
            : $"The latest stored pair spans {FormatHistoryTimestamp(previous.CreatedUtc)} to {FormatHistoryTimestamp(latest.CreatedUtc)} with {totalChangedPaths:N0} changed paths ready for review.";
    }

    private void RefreshDriftFileSampleCards()
    {
        if (!persistedDriftSnapshot.HasBaseline)
        {
            ReplaceAll(DriftFileSampleCards,
            [
                new AtlasStoredMemoryCard(
                    "WAITING FOR SAMPLE",
                    "No changed-path sample yet",
                    IsLiveMode
                        ? "Atlas needs a stored session pair before it can surface bounded changed-file examples."
                        : "Changed-path samples appear when the service can answer stored drift queries.")
            ]);

            DriftFileSampleSummaryText = "A bounded changed-path sample will appear here once Atlas can compare stored scan pairs.";
            return;
        }

        var diff = persistedSessionDiff.Found
            ? persistedSessionDiff
            : new SessionDiffResponse
            {
                Found = true,
                AddedCount = persistedDriftSnapshot.AddedCount,
                RemovedCount = persistedDriftSnapshot.RemovedCount,
                ChangedCount = persistedDriftSnapshot.ChangedCount,
                UnchangedCount = persistedDriftSnapshot.UnchangedCount
            };

        var totalChangedPaths = diff.AddedCount + diff.RemovedCount + diff.ChangedCount;
        if (persistedDiffFiles.Files.Count == 0)
        {
            ReplaceAll(DriftFileSampleCards,
            [
                new AtlasStoredMemoryCard(
                    "BOUNDED SAMPLE",
                    totalChangedPaths == 0 ? "No changed paths" : "No sample rows returned",
                    totalChangedPaths == 0
                        ? "The current drift window is stable, so there are no added, removed, or changed file rows to sample."
                        : "Atlas counted changed paths in the window, but the current bounded diff page did not include sample rows.")
            ]);

            DriftFileSampleSummaryText = totalChangedPaths == 0
                ? "The current drift window is stable, so there are no changed-file examples to review."
                : $"Atlas counted {totalChangedPaths:N0} changed paths, but the current bounded sample did not include rows.";
            return;
        }

        var files = persistedDiffFiles.Files;
        var addedFiles = files.Where(f => f.ChangeKind == "Added").Take(3).ToList();
        var removedFiles = files.Where(f => f.ChangeKind == "Removed").Take(3).ToList();
        var changedFiles = files.Where(f => f.ChangeKind == "Changed").Take(3).ToList();

        var cards = new List<AtlasStoredMemoryCard>();
        if (addedFiles.Count > 0)
        {
            cards.Add(new AtlasStoredMemoryCard("ADDED FILES", $"{diff.AddedCount:N0} total", $"Showing up to {addedFiles.Count} bounded examples."));
            cards.AddRange(addedFiles.Select(CreateDriftFileSampleCard));
        }
        if (removedFiles.Count > 0)
        {
            cards.Add(new AtlasStoredMemoryCard("REMOVED FILES", $"{diff.RemovedCount:N0} total", $"Showing up to {removedFiles.Count} bounded examples."));
            cards.AddRange(removedFiles.Select(CreateDriftFileSampleCard));
        }
        if (changedFiles.Count > 0)
        {
            cards.Add(new AtlasStoredMemoryCard("CHANGED FILES", $"{diff.ChangedCount:N0} total", $"Showing up to {changedFiles.Count} bounded examples."));
            cards.AddRange(changedFiles.Select(CreateDriftFileSampleCard));
        }

        if (cards.Count == 0)
        {
            cards.Add(new AtlasStoredMemoryCard("BOUNDED SAMPLE", "No matching files", "The drift window has changed paths, but the bounded page did not include file rows matching any change kind."));
        }

        ReplaceAll(DriftFileSampleCards, cards);
        var sampleCount = addedFiles.Count + removedFiles.Count + changedFiles.Count;
        DriftFileSampleSummaryText = $"Showing {sampleCount:N0} bounded changed-path examples grouped by change kind from the latest stored scan pair. Atlas stays path-stable, so moves currently appear as removed plus added.";
    }

    private void RefreshDriftHotspotCards()
    {
        if (!persistedDriftSnapshot.HasBaseline)
        {
            ReplaceAll(DriftHotspotCards,
            [
                new AtlasStoredMemoryCard(
                    "WAITING FOR HOTSPOTS",
                    "No drift pattern summary yet",
                    IsLiveMode
                        ? "Atlas needs a stored scan pair before it can summarize where change is concentrating."
                        : "Drift hotspots appear when the service can answer stored scan diff queries.")
            ]);

            DriftHotspotSummaryText = "Drift hotspots will appear here once Atlas can summarize changed-file patterns.";
            return;
        }

        var diff = persistedSessionDiff.Found
            ? persistedSessionDiff
            : new SessionDiffResponse
            {
                Found = true,
                AddedCount = persistedDriftSnapshot.AddedCount,
                RemovedCount = persistedDriftSnapshot.RemovedCount,
                ChangedCount = persistedDriftSnapshot.ChangedCount,
                UnchangedCount = persistedDriftSnapshot.UnchangedCount
            };
        var files = persistedDiffFiles.Found ? persistedDiffFiles.Files : new List<DiffFileSummary>();
        if (files.Count == 0)
        {
            var totalChanged = diff.AddedCount + diff.RemovedCount + diff.ChangedCount;
            ReplaceAll(DriftHotspotCards,
            [
                new AtlasStoredMemoryCard(
                    "SUMMARY ONLY",
                    totalChanged == 0 ? "Stable drift window" : $"{totalChanged:N0} changed paths",
                    totalChanged == 0
                        ? "The latest stored pair is stable, so there are no hotspots to summarize."
                        : "Atlas has drift counts for the latest pair, but it still needs bounded changed-file rows to summarize hotspot patterns.")
            ]);

            DriftHotspotSummaryText = totalChanged == 0
                ? "The latest stored scan pair is stable, so Atlas has no drift hotspots to summarize."
                : $"Atlas can prove {totalChanged:N0} changed paths in the latest window, but hotspot summaries still need bounded changed-file rows.";
            return;
        }

        var categoryGroups = files
            .GroupBy(file => ClassifyCategory(Path.GetExtension(file.Path)))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        var extensionGroups = files
            .Select(file => Path.GetExtension(file.Path))
            .Where(static extension => !string.IsNullOrWhiteSpace(extension))
            .GroupBy(extension => extension.ToLowerInvariant())
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        var addedCount = files.Count(file => string.Equals(file.ChangeKind, "Added", StringComparison.OrdinalIgnoreCase));
        var removedCount = files.Count(file => string.Equals(file.ChangeKind, "Removed", StringComparison.OrdinalIgnoreCase));
        var changedCount = files.Count(file => string.Equals(file.ChangeKind, "Changed", StringComparison.OrdinalIgnoreCase));
        var dominantBias = new[]
        {
            ("Added", addedCount),
            ("Removed", removedCount),
            ("Changed", changedCount)
        }
        .OrderByDescending(static pair => pair.Item2)
        .ThenBy(static pair => pair.Item1, StringComparer.Ordinal)
        .First();
        var cards = new List<AtlasStoredMemoryCard>();

        if (categoryGroups.Count > 0)
        {
            var leadCategory = categoryGroups[0];
            cards.Add(new AtlasStoredMemoryCard(
                "HOT CATEGORY",
                $"{leadCategory.Key} ({leadCategory.Count():N0})",
                string.Join(" | ", categoryGroups.Select(group => $"{group.Key} {group.Count():N0}"))));
        }

        if (extensionGroups.Count > 0)
        {
            cards.Add(new AtlasStoredMemoryCard(
                "HOT EXTENSIONS",
                string.Join(" | ", extensionGroups.Select(group => $"{group.Key} {group.Count():N0}")),
                "Atlas is grouping bounded changed-file rows by extension so planning can see which file types are shifting most in the current drift window."));
        }

        cards.Add(new AtlasStoredMemoryCard(
            "CHANGE BIAS",
            $"{dominantBias.Item1} leaning",
            $"{addedCount:N0} added, {removedCount:N0} removed, and {changedCount:N0} changed rows are present in the current bounded sample."));

        var sensitiveCount = files.Count(file => ClassifySensitivity(file.Path) >= SensitivityLevel.High);
        var archivesCount = files.Count(file => string.Equals(ClassifyCategory(Path.GetExtension(file.Path)), "Archives", StringComparison.OrdinalIgnoreCase));
        var documentsCount = files.Count(file => string.Equals(ClassifyCategory(Path.GetExtension(file.Path)), "Documents", StringComparison.OrdinalIgnoreCase));
        var pressureTitle = sensitiveCount > 0
            ? $"{sensitiveCount:N0} sensitive paths"
            : archivesCount > 0
                ? $"{archivesCount:N0} archive shifts"
                : documentsCount > 0
                    ? $"{documentsCount:N0} document shifts"
                    : $"{files.Count:N0} bounded rows";
        var pressureDetail = sensitiveCount > 0
            ? "Sensitive-looking paths are showing up in the bounded drift sample, so Atlas should keep plan review posture conservative."
            : archivesCount > 0
                ? "Archive churn is showing up in the current drift sample, which often points to download, installer, or cleanup pressure."
                : documentsCount > 0
                    ? "Document churn is visible in the current drift sample, which can shape how Atlas explains organization plans."
                    : "The current drift sample is broad rather than concentrated in one obvious pressure lane.";
        cards.Add(new AtlasStoredMemoryCard("PLAN PRESSURE", pressureTitle, pressureDetail));

        ReplaceAll(DriftHotspotCards, cards);

        var hotspotLead = categoryGroups.Count == 0 ? "mixed drift" : $"{categoryGroups[0].Key.ToLowerInvariant()} changes";
        DriftHotspotSummaryText = $"Atlas is summarizing the newest drift window through {hotspotLead}, {dominantBias.Item1.ToLowerInvariant()} bias, and {cards.Count:N0} hotspot lenses built from bounded changed-file evidence.";
    }

    private void RefreshScanProvenanceSignals()
    {
        if (currentScan.Inventory.Count == 0 && !persistedInventorySnapshot.HasSession)
        {
            ReplaceAll(ScanProvenanceSignals,
            [
                new AtlasSignalCard(
                    "SESSION ORIGIN",
                    "Awaiting scan",
                    "Atlas needs a live or preview scan before it can explain where the current session came from.")
            ]);

            ScanProvenanceSummaryText = "Scan provenance will appear here once Atlas has enough live or stored scan evidence to explain session origin.";
            return;
        }

        var currentOriginValue = currentScan.Inventory.Count == 0
            ? "No active scan"
            : IsLiveMode
                ? "Live service scan"
                : "Preview-local scan";
        var currentOriginDetail = currentScan.Inventory.Count == 0
            ? "No active inventory is loaded in the shell right now."
            : IsLiveMode
                ? $"The current session was loaded through the privileged service path and is tracking {currentScan.Inventory.Count:N0} files."
                : $"The current session is coming from local preview heuristics and is tracking {currentScan.Inventory.Count:N0} files without mutation access.";

        var storedTrigger = FirstNonEmpty(persistedInventoryDetail.Trigger, persistedInventorySnapshot.Trigger);
        var storedBuildMode = FirstNonEmpty(persistedInventoryDetail.BuildMode, persistedInventorySnapshot.BuildMode);
        var storedDeltaSource = FirstNonEmpty(persistedInventoryDetail.DeltaSource, persistedInventorySnapshot.DeltaSource);
        var storedBaseline = FirstNonEmpty(persistedInventoryDetail.BaselineSessionId, persistedInventorySnapshot.BaselineSessionId);
        var storedNote = FirstNonEmpty(persistedInventoryDetail.CompositionNote, persistedInventorySnapshot.CompositionNote);
        var storedIsTrusted = persistedInventoryDetail.Found || persistedInventorySnapshot.HasSession
            ? (persistedInventoryDetail.Found ? persistedInventoryDetail.IsTrusted : persistedInventorySnapshot.IsTrusted)
            : true;
        var storedTriggerLabel = FormatTriggerLabel(storedTrigger);
        var storedBuildModeLabel = FormatBuildModeLabel(storedBuildMode);
        var storedDeltaSourceLabel = FormatDeltaSourceLabel(storedDeltaSource, storedBuildMode);
        var storedTrustLabel = FormatTrustLabel(storedIsTrusted);
        var storedBaselineLabel = FormatBaselineLabel(storedBaseline);

        var storedOriginValue = persistedInventorySnapshot.HasSession
            ? $"{persistedInventorySessions.Sessions.Count:N0} stored sessions"
            : IsLiveMode
                ? "No stored baseline"
                : "Service offline";
        var storedOriginDetail = persistedInventorySnapshot.HasSession
            ? $"Latest stored session {ShortId(persistedInventorySnapshot.SessionId)} was captured on {FormatHistoryTimestamp(persistedInventorySnapshot.CreatedUtc)} through {storedTriggerLabel.ToLowerInvariant()}."
            : IsLiveMode
                ? "The service is reachable, but it has not retained a stored scan session yet."
                : "Stored provenance appears when the service can answer persisted inventory queries.";

        var diffModelValue = persistedDriftSnapshot.HasBaseline
            ? "Path-stable diff"
            : persistedInventorySessions.Sessions.Count > 1
                ? "Pair pending"
                : "Baseline only";
        var diffModelDetail = persistedDriftSnapshot.HasBaseline
            ? $"Atlas is comparing stored sessions {ShortId(persistedDriftSnapshot.OlderSessionId)} and {ShortId(persistedDriftSnapshot.NewerSessionId)} with added, removed, changed, and unchanged path counts."
            : persistedInventorySessions.Sessions.Count > 1
                ? "Atlas has the stored pair, but the exact diff window is not available yet."
                : "Atlas needs two stored sessions before it can prove a comparison window.";

        var evidenceDepthValue = persistedDiffFiles.Files.Count switch
        {
            0 when persistedDriftSnapshot.HasBaseline => "Summary only",
            > 0 => $"{persistedDiffFiles.Files.Count:N0} sample rows",
            _ => "No drill-in yet"
        };
        var evidenceDepthDetail = persistedDiffFiles.Files.Count switch
        {
            0 when persistedDriftSnapshot.HasBaseline => "Atlas can prove the drift counts for the latest pair, but the current bounded page has no changed-file examples loaded.",
            > 0 => "Atlas is carrying a bounded changed-file page for drill-in while keeping the review surface lightweight.",
            _ => "Changed-file drill-in appears once Atlas has both a stored pair and bounded diff rows."
        };

        var sessionOriginValue = persistedInventorySnapshot.HasSession
            ? storedTriggerLabel
            : IsLiveMode
                ? "Service scan"
                : "Local preview";
        var sessionOriginDetail = persistedInventorySnapshot.HasSession
            ? $"Latest stored session came from {storedTriggerLabel.ToLowerInvariant()} and is retained as session {ShortId(persistedInventorySnapshot.SessionId)}."
            : IsLiveMode
                ? "The latest stored session was captured through the privileged service path."
                : "The latest stored session came from local preview heuristics.";
        var compositionValue = persistedInventorySnapshot.HasSession
            ? $"{storedBuildModeLabel} / {storedTrustLabel}"
            : "Awaiting stored session";
        var compositionDetail = persistedInventorySnapshot.HasSession
            ? $"{storedDeltaSourceLabel}. {storedBaselineLabel}.{(string.IsNullOrWhiteSpace(storedNote) ? string.Empty : $" Note: {storedNote}")}"
            : "Stored composition details appear once the service retains a scan session.";
        var deltaSourceValue = persistedInventorySnapshot.HasSession
            ? storedDeltaSourceLabel
            : "Awaiting stored session";
        var deltaSourceDetail = persistedInventorySnapshot.HasSession
            ? $"Atlas recorded {storedDeltaSourceLabel.ToLowerInvariant()} for the latest stored session."
            : "Delta-source detail appears once the service retains a stored scan session.";
        var baselineValue = persistedInventorySnapshot.HasSession
            ? storedBaselineLabel
            : "Awaiting stored session";
        var baselineDetail = persistedInventorySnapshot.HasSession
            ? string.IsNullOrWhiteSpace(storedBaseline)
                ? "The latest stored session does not rely on a retained baseline link."
                : $"Latest stored session links back to {ShortId(storedBaseline)} for incremental lineage."
            : "Baseline lineage appears once the service retains a stored scan session.";

        ReplaceAll(ScanProvenanceSignals,
        [
            new AtlasSignalCard("CURRENT SESSION", currentOriginValue, currentOriginDetail),
            new AtlasSignalCard("STORED LINEAGE", storedOriginValue, storedOriginDetail),
            new AtlasSignalCard("DIFF MODEL", diffModelValue, diffModelDetail),
            new AtlasSignalCard("EVIDENCE DEPTH", evidenceDepthValue, evidenceDepthDetail),
            new AtlasSignalCard("SESSION ORIGIN", sessionOriginValue, sessionOriginDetail),
            new AtlasSignalCard("COMPOSITION TYPE", compositionValue, compositionDetail),
            new AtlasSignalCard("DELTA SOURCE", deltaSourceValue, deltaSourceDetail),
            new AtlasSignalCard("BASELINE LINK", baselineValue, baselineDetail)
        ]);

        ScanProvenanceSummaryText = persistedDriftSnapshot.HasBaseline
            ? $"Atlas can now explain the current shell origin, the latest stored session lineage, and the bounded diff model behind the current scan pair. The newest stored session is tagged as {compositionValue.ToLowerInvariant()} via {deltaSourceValue.ToLowerInvariant()}."
            : persistedInventorySnapshot.HasSession
                ? $"Atlas can explain where the current shell session came from and how the latest stored session was built: {compositionValue.ToLowerInvariant()} through {sessionOriginValue.ToLowerInvariant()} with {deltaSourceValue.ToLowerInvariant()}."
                : "Atlas can explain the current shell session origin, but it still needs stored scan lineage for deeper provenance.";
    }

    private void RefreshHistoryMetrics()
    {
        ReplaceAll(HistoryMetrics,
        [
            new AtlasMetricCard("SESSION EVENTS", $"{ActivityFeed.Count:N0}", "Recent actions and outcomes Atlas has tracked in this shell session."),
            new AtlasMetricCard("PLANNED OPS", $"{currentPlan.Plan.Operations.Count:N0}", "Operations currently staged in the most recent plan review."),
            new AtlasMetricCard("RECOVERY ASSETS", $"{latestCheckpoint.InverseOperations.Count + latestCheckpoint.QuarantineItems.Count:N0}", "Inverse operations and direct restore items currently in memory."),
            new AtlasMetricCard("STORED SCANS", $"{persistedInventorySessions.Sessions.Count:N0}", persistedInventorySnapshot.HasSession
                ? "Recent persisted inventory sessions are available from the service read path."
                : "No persisted inventory sessions are available yet."),
            new AtlasMetricCard("STORED PLANS", $"{persistedHistory.RecentPlans.Count:N0}", "Recent persisted plans returned by the service history snapshot."),
            new AtlasMetricCard("STORED TRACES", $"{persistedHistory.RecentTraces.Count:N0}", "Recent planning or voice traces retained in service-backed memory."),
            new AtlasMetricCard("SERVICE MODE", IsLiveMode ? "Live" : "Preview", IsLiveMode
                ? "The service is reachable, so persisted history can grow beyond the current shell session."
                : "The shell is still simulating safely while the privileged service remains offline.")
        ]);

        RecentActivitySummaryText = ActivityFeed.Count == 0
            ? "Atlas has not recorded any session activity yet."
            : string.Join("\n\n", ActivityFeed
                .Take(3)
                .Select(entry => $"{entry.Timestamp}  {entry.Title}\n{entry.Detail}"));

        PlanSignalSummaryText = PlanSignals.Count == 0
            ? "Plan assurance will appear here after Atlas drafts and scores a reviewed plan."
            : string.Join("\n\n", PlanSignals
                .Take(3)
                .Select(signal => $"{signal.Eyebrow}: {signal.Value}\n{signal.Detail}"));

        UndoSignalSummaryText = UndoSignals.Count == 0
            ? "Recovery assurance will appear here after Atlas stages inverse operations or quarantine restores."
            : string.Join("\n\n", UndoSignals
                .Take(3)
                .Select(signal => $"{signal.Eyebrow}: {signal.Value}\n{signal.Detail}"));

        QuarantineSummaryText = QuarantineEntries.Count == 0
            ? persistedHistory.RecentQuarantine.Count > 0
                ? $"{persistedHistory.RecentQuarantine.Count:N0} stored quarantine items are available from service-backed memory."
                : "No restore-ready quarantine items are currently staged in this session."
            : string.Join("\n\n", QuarantineEntries
                .Take(3)
                .Select(item => $"{item.Name}\n{item.OriginalPath}\n{item.Detail}"));
    }

    private void RefreshPlanSignals()
    {
        var risk = currentPlan.Plan.RiskSummary;
        var blockedCount = risk.BlockedReasons.Count;
        var approvalValue = blockedCount > 0
            ? "Blocked"
            : risk.ApprovalRequirement switch
            {
                ApprovalRequirement.ExplicitApproval => "Explicit approval",
                ApprovalRequirement.Review => "Review gate",
                _ => "Preview ready"
            };

        var approvalDetail = blockedCount > 0
            ? $"{blockedCount:N0} blocked reasons need attention before Atlas should even ask the service to run this batch."
            : risk.ApprovalRequirement switch
            {
                ApprovalRequirement.ExplicitApproval => "This batch crosses a higher-risk threshold and should stay human-approved even after preview.",
                ApprovalRequirement.Review => "Atlas can explain the plan now, but a review gate still stands before service execution.",
                _ => "The plan is structurally clean enough for preview, but execution still remains service-only."
            };

        var readinessValue = !IsLiveMode
            ? "Preview only"
            : CanExecutePlan
                ? "Service armed"
                : "Service gated";

        var readinessDetail = !IsLiveMode
            ? "The shell can draft and simulate, but real file mutation remains blocked until the privileged service is reachable."
            : CanExecutePlan
                ? "Atlas can submit this batch to the service, but only after the user approves the preview."
                : "The service is online, but Atlas is still withholding execution because the plan is incomplete or blocked.";

        var reversibilityValue = $"{risk.ReversibilityScore:P0}";
        var reversibilityDetail = risk.ReversibilityScore >= 0.9d
            ? "Rollback posture is strong: Atlas expects inverse operations and quarantine coverage to carry most of the recovery load."
            : risk.ReversibilityScore >= 0.7d
                ? "Rollback posture is moderate: review the undo story closely before asking Atlas to execute."
                : "Rollback posture is weak: this plan needs extra scrutiny before it should move any files.";

        var sensitivityValue = risk.SensitivityScore >= 0.7d
            ? "High"
            : risk.SensitivityScore >= 0.35d
                ? "Moderate"
                : "Low";

        var sensitivityDetail = $"Sensitivity score {risk.SensitivityScore:P0}, sync risk {risk.SyncRisk:P0}, planner confidence {risk.Confidence:P0}.";

        ReplaceAll(PlanSignals,
        [
            new AtlasSignalCard("APPROVAL POSTURE", approvalValue, approvalDetail),
            new AtlasSignalCard("SERVICE READINESS", readinessValue, readinessDetail),
            new AtlasSignalCard("REVERSIBILITY", reversibilityValue, reversibilityDetail),
            new AtlasSignalCard("SENSITIVITY", sensitivityValue, sensitivityDetail)
        ]);

        ReplaceAll(PlanBlockedReasons, risk.BlockedReasons.Count == 0
            ? ["No blocked reasons are active on the current plan."]
            : risk.BlockedReasons);
    }

    private void RefreshUndoSignals()
    {
        var recoveryMode = !IsLiveMode
            ? "Preview only"
            : CanExecuteUndo
                ? "Service armed"
                : "Awaiting checkpoint";

        var recoveryModeDetail = !IsLiveMode
            ? "Atlas can explain the rollback path locally, but the service is still required to replay inverse operations."
            : CanExecuteUndo
                ? "The latest checkpoint can be replayed through the service if the user approves the rollback."
                : "Atlas does not yet have enough checkpoint data to ask the service for a real rollback.";

        var coverageValue = latestCheckpoint.InverseOperations.Count == 0 && latestCheckpoint.QuarantineItems.Count == 0
            ? "Not armed"
            : $"{latestCheckpoint.InverseOperations.Count + latestCheckpoint.QuarantineItems.Count:N0} assets";

        var coverageDetail = latestCheckpoint.QuarantineItems.Count > 0
            ? $"{latestCheckpoint.InverseOperations.Count:N0} inverse operations plus {latestCheckpoint.QuarantineItems.Count:N0} direct restore candidates are available."
            : $"{latestCheckpoint.InverseOperations.Count:N0} inverse operations are staged, with no file-level quarantine restores in this checkpoint yet.";

        var checkpointValue = latestCheckpoint.VssSnapshotReferences.Count > 0
            ? "Snapshot-backed"
            : latestCheckpoint.InverseOperations.Count > 0 || latestCheckpoint.QuarantineItems.Count > 0
                ? "Journal-backed"
                : "No checkpoint";

        var checkpointDetail = latestCheckpoint.VssSnapshotReferences.Count > 0
            ? $"{latestCheckpoint.VssSnapshotReferences.Count:N0} heavyweight snapshot references accompany the normal undo journal."
            : "Atlas is currently relying on inverse operations, quarantine items, and checkpoint notes rather than VSS snapshots.";

        var restoreConfidenceValue = latestCheckpoint.QuarantineItems.Count > 0
            ? "File restore ready"
            : latestCheckpoint.InverseOperations.Count > 0
                ? "Batch restore ready"
                : "No restore path";

        var restoreConfidenceDetail = latestCheckpoint.Notes.Count > 0
            ? string.Join(" ", latestCheckpoint.Notes)
            : "Atlas will surface recovery notes here once a dry run or live execution has produced checkpoint commentary.";

        ReplaceAll(UndoSignals,
        [
            new AtlasSignalCard("RECOVERY MODE", recoveryMode, recoveryModeDetail),
            new AtlasSignalCard("RESTORE COVERAGE", coverageValue, coverageDetail),
            new AtlasSignalCard("CHECKPOINT DEPTH", checkpointValue, checkpointDetail),
            new AtlasSignalCard("RECOVERY NOTES", restoreConfidenceValue, restoreConfidenceDetail)
        ]);
    }

    private IReadOnlyList<AtlasStructureGroupCard> BuildCurrentStructureGroups()
    {
        if (currentScan.Inventory.Count == 0)
        {
            return
            [
                new AtlasStructureGroupCard(
                    "Awaiting inventory",
                    "0 files",
                    "Run a scan to map the mutable roots before Atlas drafts any structural changes.",
                    "This lane will summarize current category spread and folder sprawl.")
            ];
        }

        return currentScan.Inventory
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Category) ? "Other" : item.Category)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(group =>
            {
                var directories = group.Select(item => Path.GetDirectoryName(item.Path))
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var totalBytes = group.Sum(item => item.SizeBytes);
                var sensitiveCount = group.Count(item => item.Sensitivity >= SensitivityLevel.High);
                var syncCount = group.Count(item => item.IsSyncManaged);
                var samplePaths = directories.Take(2).Select(FormatDisplayPath).ToList();

                var detail = sensitiveCount > 0
                    ? $"{FormatBytes(totalBytes)} across {directories.Count:N0} folders, with {sensitiveCount:N0} sensitive items held behind extra review."
                    : syncCount > 0
                        ? $"{FormatBytes(totalBytes)} across {directories.Count:N0} folders, with {syncCount:N0} sync-managed items excluded by default."
                        : $"{FormatBytes(totalBytes)} spread across {directories.Count:N0} folders in the mutable workspace.";

                var sample = samplePaths.Count > 0
                    ? $"Seen in {string.Join(" | ", samplePaths)}"
                    : "No folder sample is available yet.";

                return new AtlasStructureGroupCard(
                    group.Key,
                    $"{group.Count():N0} files",
                    detail,
                    sample);
            })
            .ToList();
    }

    private IReadOnlyList<AtlasStructureGroupCard> BuildProposedStructureGroups()
    {
        if (currentPlan.Plan.Operations.Count == 0)
        {
            return
            [
                new AtlasStructureGroupCard(
                    "Awaiting plan",
                    "0 changes",
                    "Ask Atlas to draft a plan and the proposed layout lane will show category anchors, placements, and quarantine staging.",
                    "Nothing will execute until the service validates the final batch.")
            ];
        }

        return currentPlan.Plan.Operations
            .Select(operation => new
            {
                GroupTitle = ResolveOperationGroupTitle(operation),
                Operation = operation
            })
            .GroupBy(item => item.GroupTitle, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(group =>
            {
                var createCount = group.Count(item => item.Operation.Kind == OperationKind.CreateDirectory);
                var moveCount = group.Count(item => item.Operation.Kind is OperationKind.MovePath or OperationKind.RenamePath);
                var quarantineCount = group.Count(item => item.Operation.Kind == OperationKind.DeleteToQuarantine);
                var reviewCount = group.Count(item =>
                    item.Operation.Kind == OperationKind.DeleteToQuarantine
                    || item.Operation.Sensitivity >= SensitivityLevel.High);

                var targets = group
                    .Select(item => string.IsNullOrWhiteSpace(item.Operation.DestinationPath) ? item.Operation.SourcePath : item.Operation.DestinationPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .Select(FormatDisplayPath)
                    .ToList();

                var value = quarantineCount > 0 && moveCount == 0 && createCount == 0
                    ? $"{quarantineCount:N0} items"
                    : $"{createCount + moveCount + quarantineCount:N0} changes";

                var detail = string.Equals(group.Key, "Quarantine review", StringComparison.OrdinalIgnoreCase)
                    ? $"{quarantineCount:N0} duplicate or cleanup candidates stay reversible in quarantine first."
                    : $"{moveCount:N0} incoming placements, {createCount:N0} anchor folders, {reviewCount:N0} review-gated actions.";

                var sample = targets.Count > 0
                    ? $"Landing in {string.Join(" | ", targets)}"
                    : "Atlas will add a destination sample after the next plan.";

                return new AtlasStructureGroupCard(group.Key, value, detail, sample);
            })
            .ToList();
    }

    private string BuildIdleStructureNarrative()
    {
        if (currentScan.Inventory.Count == 0)
        {
            return "Atlas will surface a before-and-after structure narrative once a plan exists.";
        }

        var categoryCount = currentScan.Inventory
            .Select(item => string.IsNullOrWhiteSpace(item.Category) ? "Other" : item.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var folderCount = currentScan.Inventory
            .Select(item => Path.GetDirectoryName(item.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return $"Atlas is seeing {currentScan.Inventory.Count:N0} mutable files across {categoryCount:N0} visible categories and {folderCount:N0} folders. Draft a plan to preview how that spread collapses into calmer anchors before anything executes.";
    }

    private string BuildPlanDiffNarrative(int createCount, int moveCount, int quarantineCount, int reviewCount)
    {
        var currentCategoryCount = currentScan.Inventory
            .Select(item => string.IsNullOrWhiteSpace(item.Category) ? "Other" : item.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var proposedAnchorCount = ProposedStructureGroups.Count(group =>
            !string.Equals(group.Title, "Quarantine review", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(group.Title, "Awaiting plan", StringComparison.OrdinalIgnoreCase));

        return $"Atlas is reshaping {moveCount:N0} placements across {currentCategoryCount:N0} visible categories into {proposedAnchorCount:N0} calmer destination anchors, backed by {createCount:N0} new folders and {quarantineCount:N0} quarantine-first items. {reviewCount:N0} operations still sit behind review gates before execution.";
    }

    private static string ResolveOperationGroupTitle(PlanOperation operation)
    {
        if (operation.Kind == OperationKind.DeleteToQuarantine)
        {
            return "Quarantine review";
        }

        if (operation.Kind == OperationKind.ApplyOptimizationFix)
        {
            return "Optimization fixes";
        }

        var targetPath = string.IsNullOrWhiteSpace(operation.DestinationPath) ? operation.SourcePath : operation.DestinationPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return "General staging";
        }

        var atlasAnchor = TryGetAtlasAnchor(targetPath);
        if (!string.IsNullOrWhiteSpace(atlasAnchor))
        {
            return atlasAnchor;
        }

        if (operation.Kind == OperationKind.CreateDirectory)
        {
            var createdFolder = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(createdFolder))
            {
                return createdFolder;
            }
        }

        var parentFolder = Path.GetDirectoryName(targetPath);
        var parentName = string.IsNullOrWhiteSpace(parentFolder) ? string.Empty : Path.GetFileName(parentFolder);
        return string.IsNullOrWhiteSpace(parentName) ? "General staging" : parentName;
    }

    private static string? TryGetAtlasAnchor(string path)
    {
        var segments = path
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], "Atlas Organized", StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 1];
            }
        }

        return null;
    }

    private string FormatDisplayPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "No path available.";
        }

        foreach (var root in profile.MutableRoots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootLabel = Path.GetFileName(trimmedRoot);
            if (string.IsNullOrWhiteSpace(rootLabel))
            {
                rootLabel = trimmedRoot;
            }

            var relative = path[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.IsNullOrWhiteSpace(relative) ? rootLabel : $@"{rootLabel}\{relative}";
        }

        return path.Length <= 68 ? path : $"{path[..26]}...{path[^32..]}";
    }

    private void ApplyCommandPreview(
        string interpretation,
        string detail,
        string origin,
        string workspace,
        string reviewLabel,
        string reviewDetail,
        string nextStepLabel,
        string nextStepDetail)
    {
        CommandInterpretationText = interpretation;
        CommandInterpretationDetailText = detail;
        CommandOriginLabel = origin;
        CommandWorkspaceLabel = workspace;
        CommandReviewLabel = reviewLabel;
        CommandReviewDetailText = reviewDetail;
        CommandNextStepLabel = nextStepLabel;
        CommandNextStepDetailText = nextStepDetail;
    }

    private string BuildNextStepDetail(string routeTag, bool requiresConfirmation)
    {
        var modeDetail = IsLiveMode
            ? "The service is connected, but execution still remains gated by plan review."
            : "Atlas is still in preview mode, so the shell can explain and simulate while execution stays blocked.";

        return routeTag switch
        {
            "optimization" => $"{modeDetail} Run the optimization scan to separate auto-fix candidates from recommendation-only findings.",
            "undo" => $"{modeDetail} Preview the checkpoint first, then decide whether the rollback or restore should be replayed.",
            "settings" => $"{modeDetail} Review the guardrails, then return to the workspace that best matches the request.",
            _ => requiresConfirmation
                ? $"{modeDetail} Draft the plan, inspect the diff canvas, and expect extra review before any destructive step becomes eligible."
                : $"{modeDetail} Draft the plan first, then inspect the proposed structure and rollback posture together."
        };
    }

    private static string ResolveCommandRouteTag(string commandText, string fallbackTag)
    {
        if (ContainsAny(commandText, "undo", "restore", "rollback", "quarantine", "recover"))
        {
            return "undo";
        }

        if (ContainsAny(commandText, "optimize", "optimization", "startup", "background", "temp", "temporary", "cache", "performance", "slow"))
        {
            return "optimization";
        }

        if (ContainsAny(commandText, "policy", "sensitivity", "sync", "retention", "threshold", "root", "roots", "protected path", "upload"))
        {
            return "settings";
        }

        return string.IsNullOrWhiteSpace(fallbackTag) ? "plans" : fallbackTag switch
        {
            "dashboard" => "plans",
            _ => fallbackTag
        };
    }

    private static string ResolveWorkspaceLabel(string routeTag) =>
        routeTag switch
        {
            "optimization" => "Optimization Center",
            "undo" => "Undo Timeline",
            "settings" => "Policy Studio",
            "plans" => "Plan Review",
            _ => "Command Deck"
        };

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private ScanResponse BuildPreviewScan(CancellationToken cancellationToken)
    {
        var response = new ScanResponse();
        foreach (var drive in DriveInfo.GetDrives().Where(static drive => drive.IsReady))
        {
            response.Volumes.Add(new VolumeSnapshot
            {
                RootPath = drive.RootDirectory.FullName,
                DriveFormat = drive.DriveFormat,
                DriveType = drive.DriveType.ToString(),
                IsReady = drive.IsReady,
                TotalSizeBytes = drive.TotalSize,
                FreeSpaceBytes = drive.AvailableFreeSpace
            });
        }

        const int maxFiles = 1200;
        foreach (var root in profile.MutableRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root) || pathSafetyClassifier.IsExcludedPath(profile, root))
            {
                continue;
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.System | FileAttributes.Device | FileAttributes.Offline
            };

            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (response.Inventory.Count >= maxFiles)
                {
                    break;
                }

                if (pathSafetyClassifier.IsProtectedPath(profile, file) || pathSafetyClassifier.IsExcludedPath(profile, file))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(file);
                    var item = new FileInventoryItem
                    {
                        Path = info.FullName,
                        Name = info.Name,
                        Extension = info.Extension,
                        Category = ClassifyCategory(info.Extension),
                        MimeType = info.Extension.Trim('.').ToLowerInvariant(),
                        SizeBytes = info.Length,
                        LastModifiedUnixTimeSeconds = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                        Sensitivity = ClassifySensitivity(info.FullName),
                        IsSyncManaged = profile.ExcludeSyncFoldersByDefault && pathSafetyClassifier.IsSyncManaged(profile, info.FullName),
                        IsDuplicateCandidate = info.Length > 0,
                        IsProtectedByUser = false
                    };

                    if (!item.IsSyncManaged || !profile.ExcludeSyncFoldersByDefault)
                    {
                        response.Inventory.Add(item);
                    }
                }
                catch
                {
                    // Skip transient files while assembling a local preview.
                }
            }

            if (response.Inventory.Count >= maxFiles)
            {
                break;
            }
        }

        response.Duplicates.AddRange(
            response.Inventory
                .Where(static item => item.SizeBytes > 0)
                .GroupBy(item => $"{item.Name}|{item.SizeBytes}")
                .Where(static group => group.Count() > 1)
                .Select(group =>
                {
                    var canonical = group
                        .OrderByDescending(static item => item.Sensitivity)
                        .ThenBy(static item => item.Path.Length)
                        .First();

                    return new DuplicateGroup
                    {
                        CanonicalPath = canonical.Path,
                        Confidence = 0.99d,
                        Paths = group.Select(static item => item.Path).ToList()
                    };
                })
                .Take(8));

        response.FilesScanned = response.Inventory.Count;
        return response;
    }

    private PlanResponse BuildPreviewPlan(string intent)
    {
        var plan = new PlanGraph
        {
            Scope = intent,
            Rationale = "Local review-only fallback plan built from the current mutable inventory.",
            EstimatedBenefit = "Reduce loose-file clutter, surface conservative duplicate review, and create calmer category anchors.",
            RollbackStrategy = "Inverse operations plus quarantine restores.",
            Categories = currentScan.Inventory
                .Select(static item => item.Category)
                .Where(static category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList()
        };

        var anchorRoot = profile.MutableRoots.FirstOrDefault(path => Directory.Exists(path))
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        foreach (var category in plan.Categories)
        {
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.CreateDirectory,
                DestinationPath = Path.Combine(anchorRoot, "Atlas Organized", SanitizeCategory(category)),
                Description = $"Create a landing folder for {category} items inside the first mutable root.",
                Confidence = 0.93d,
                Sensitivity = SensitivityLevel.Low
            });
        }

        var moveCandidates = currentScan.Inventory
            .Where(item => pathSafetyClassifier.IsMutablePath(profile, item.Path))
            .Where(item => item.Sensitivity <= SensitivityLevel.Medium)
            .Where(item => !item.IsSyncManaged)
            .Take(6);

        foreach (var candidate in moveCandidates)
        {
            var category = string.IsNullOrWhiteSpace(candidate.Category) ? "Other" : candidate.Category;
            var destination = Path.Combine(anchorRoot, "Atlas Organized", SanitizeCategory(category), candidate.Name);
            if (string.Equals(candidate.Path, destination, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.MovePath,
                SourcePath = candidate.Path,
                DestinationPath = destination,
                Description = $"Move a loose {category.ToLowerInvariant()} file into a cleaner category folder.",
                Confidence = 0.82d,
                Sensitivity = candidate.Sensitivity
            });
        }

        foreach (var duplicate in currentScan.Duplicates.Where(group => group.Confidence >= profile.DuplicateAutoDeleteConfidenceThreshold).Take(4))
        {
            foreach (var duplicatePath in duplicate.Paths.Where(path => !string.Equals(path, duplicate.CanonicalPath, StringComparison.OrdinalIgnoreCase)).Take(2))
            {
                plan.Operations.Add(new PlanOperation
                {
                    Kind = OperationKind.DeleteToQuarantine,
                    SourcePath = duplicatePath,
                    Description = "Stage a duplicate candidate for quarantine after explicit review.",
                    Confidence = duplicate.Confidence,
                    MarksSafeDuplicate = true,
                    Sensitivity = SensitivityLevel.Low,
                    GroupId = duplicate.GroupId
                });
            }
        }

        var reviewCount = plan.Operations.Count(operation => operation.Kind == OperationKind.DeleteToQuarantine);
        plan.RequiresReview = reviewCount > 0;
        plan.RiskSummary = new RiskEnvelope
        {
            SensitivityScore = plan.Operations.Any(operation => operation.Sensitivity >= SensitivityLevel.High) ? 0.6d : 0.25d,
            SystemScore = 0.12d,
            SyncRisk = 0.08d,
            ReversibilityScore = 0.94d,
            Confidence = 0.81d,
            ApprovalRequirement = reviewCount > 0 ? ApprovalRequirement.Review : ApprovalRequirement.None
        };

        return new PlanResponse
        {
            Plan = plan,
            Summary = $"Prepared {plan.Operations.Count:N0} reversible operations around '{intent}'. Review gates are active for duplicate staging."
        };
    }

    private OptimizationResponse BuildPreviewOptimizationResponse(CancellationToken cancellationToken)
    {
        var response = new OptimizationResponse();

        var tempPath = Path.GetTempPath();
        if (Directory.Exists(tempPath))
        {
            long tempBytes = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(tempPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        tempBytes += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Ignore transient temp files.
                    }
                }
            }
            catch
            {
                // Ignore inaccessible temp subdirectories in preview mode.
            }

            if (tempBytes > 150L * 1024 * 1024)
            {
                response.Findings.Add(new OptimizationFinding
                {
                    Kind = OptimizationKind.TemporaryFiles,
                    Target = tempPath,
                    Evidence = $"Temporary files currently occupy about {FormatBytes(tempBytes)}.",
                    CanAutoFix = true,
                    RequiresApproval = false,
                    RollbackPlan = "No rollback required for transient temp data."
                });
            }
        }

        foreach (var drive in DriveInfo.GetDrives().Where(static drive => drive.IsReady))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var usage = 1d - ((double)drive.AvailableFreeSpace / drive.TotalSize);
            if (usage >= 0.85d)
            {
                response.Findings.Add(new OptimizationFinding
                {
                    Kind = OptimizationKind.LowDiskPressure,
                    Target = drive.RootDirectory.FullName,
                    Evidence = $"Drive usage is about {usage:P0}.",
                    CanAutoFix = false,
                    RequiresApproval = true,
                    RollbackPlan = "Recommendation only."
                });
            }
        }

        var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (Directory.Exists(startupPath))
        {
            var startupFiles = Directory.EnumerateFiles(startupPath).Take(10).ToList();
            if (startupFiles.Count > 0)
            {
                response.Findings.Add(new OptimizationFinding
                {
                    Kind = OptimizationKind.UserStartupEntry,
                    Target = startupPath,
                    Evidence = $"{startupFiles.Count:N0} startup items were found in the user startup folder.",
                    CanAutoFix = true,
                    RequiresApproval = true,
                    RollbackPlan = "Move shortcuts back into the startup folder if needed."
                });
            }
        }

        return response;
    }

    private ExecutionBatch BuildBatchFromCurrentPlan(bool isDryRun)
    {
        return new ExecutionBatch
        {
            PlanId = currentPlan.Plan.PlanId,
            RequiresCheckpoint = currentPlan.Plan.Operations.Count > 10 || currentPlan.Plan.Operations.Any(static operation => operation.Kind == OperationKind.DeleteToQuarantine),
            IsDryRun = isDryRun,
            Operations = currentPlan.Plan.Operations.Select(CloneOperation).ToList(),
            EstimatedImpact = currentPlan.Plan.EstimatedBenefit,
            TouchedVolumes = currentPlan.Plan.Operations
                .Select(operation => Path.GetPathRoot(string.IsNullOrWhiteSpace(operation.DestinationPath) ? operation.SourcePath : operation.DestinationPath))
                .Where(static root => !string.IsNullOrWhiteSpace(root))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private ExecutionResponse BuildPreviewExecutionResponse(ExecutionBatch batch)
    {
        var inverseOperations = new List<InverseOperation>();
        var quarantineItems = new List<QuarantineItem>();

        foreach (var operation in batch.Operations)
        {
            switch (operation.Kind)
            {
                case OperationKind.CreateDirectory:
                    inverseOperations.Add(new InverseOperation
                    {
                        Kind = OperationKind.DeleteToQuarantine,
                        SourcePath = operation.DestinationPath,
                        Description = $"Remove the preview-created folder {operation.DestinationPath}."
                    });
                    break;
                case OperationKind.MovePath:
                case OperationKind.RenamePath:
                    inverseOperations.Add(new InverseOperation
                    {
                        Kind = OperationKind.MovePath,
                        SourcePath = operation.DestinationPath,
                        DestinationPath = operation.SourcePath,
                        Description = $"Return {operation.DestinationPath} to {operation.SourcePath}."
                    });
                    break;
                case OperationKind.DeleteToQuarantine:
                    var quarantineItem = new QuarantineItem
                    {
                        OriginalPath = operation.SourcePath,
                        CurrentPath = $@"PreviewQuarantine\{Path.GetFileName(operation.SourcePath)}",
                        PlanId = batch.PlanId,
                        Reason = operation.Description,
                        RetentionUntilUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(QuarantineRetentionDays).ToUnixTimeSeconds()
                    };
                    quarantineItems.Add(quarantineItem);
                    inverseOperations.Add(new InverseOperation
                    {
                        Kind = OperationKind.RestoreFromQuarantine,
                        SourcePath = quarantineItem.CurrentPath,
                        DestinationPath = quarantineItem.OriginalPath,
                        Description = $"Restore {quarantineItem.OriginalPath} from preview quarantine."
                    });
                    break;
            }
        }

        return new ExecutionResponse
        {
            Success = true,
            Messages =
            [
                $"Previewed {batch.Operations.Count:N0} operations without mutating the filesystem.",
                $"Generated {inverseOperations.Count:N0} rollback steps for review."
            ],
            UndoCheckpoint = new UndoCheckpoint
            {
                BatchId = batch.BatchId,
                InverseOperations = inverseOperations,
                QuarantineItems = quarantineItems,
                Notes =
                [
                    "Preview-only rollback story.",
                    "Connect the service to persist this checkpoint and execute for real."
                ]
            }
        };
    }

    private static PlanOperation CloneOperation(PlanOperation operation) =>
        new()
        {
            OperationId = operation.OperationId,
            Kind = operation.Kind,
            SourcePath = operation.SourcePath,
            DestinationPath = operation.DestinationPath,
            Description = operation.Description,
            Confidence = operation.Confidence,
            MarksSafeDuplicate = operation.MarksSafeDuplicate,
            Sensitivity = operation.Sensitivity,
            GroupId = operation.GroupId,
            OptimizationKind = operation.OptimizationKind
        };

    private SensitivityLevel ClassifySensitivity(string path)
    {
        if (profile.ProtectedKeywords.Any(keyword => path.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return SensitivityLevel.High;
        }

        if (path.EndsWith(".kdbx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
        {
            return SensitivityLevel.Critical;
        }

        if (path.Contains("finance", StringComparison.OrdinalIgnoreCase)
            || path.Contains("legal", StringComparison.OrdinalIgnoreCase)
            || path.Contains("medical", StringComparison.OrdinalIgnoreCase))
        {
            return SensitivityLevel.High;
        }

        return SensitivityLevel.Low;
    }

    private static string ClassifyCategory(string extension) =>
        CategoryByExtension.TryGetValue(extension, out var category) ? category : "Other";

    private void SyncProfileCollections()
    {
        ReplaceAll(MutableRoots, profile.MutableRoots);
        ReplaceAll(ProtectedPaths, profile.ProtectedPaths);
        RefreshInventorySignals();
        OnPropertyChanged(nameof(MutableRootsSummaryText));
        OnPropertyChanged(nameof(ProtectedPathsSummaryText));
    }

    private static IReadOnlyList<string> BuildCommandSuggestions() =>
    [
        "Organize Downloads into clearer categories and keep duplicate deletions quarantined.",
        "Review screenshots and move them into Pictures\\Screenshots without touching sync folders.",
        "Find safe duplicate PDFs in Documents and draft a quarantine-first cleanup plan.",
        "Inspect startup clutter and temporary file pressure before suggesting safe optimizations.",
        "Show the latest rollback story and help me restore a quarantined file."
    ];

    private void AddActivity(string title, string description)
    {
        ActivityFeed.Insert(0, new AtlasActivityEntry(DateTime.Now.ToString("g"), title, description));
        while (ActivityFeed.Count > 10)
        {
            ActivityFeed.RemoveAt(ActivityFeed.Count - 1);
        }

        RefreshHistoryMetrics();
    }

    private static string SanitizeCategory(string category)
    {
        var cleaned = new string(category.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Other" : cleaned;
    }

    private static string BuildCollectionSummary(IEnumerable<string> values, string noun)
    {
        var items = values.Where(static value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (items.Count == 0)
        {
            return $"No {noun}s are configured.";
        }

        var visible = items.Take(3).ToList();
        var summary = string.Join("\n", visible);
        var remaining = items.Count - visible.Count;
        return remaining > 0
            ? $"{summary}\n+ {remaining:N0} more"
            : summary;
    }

    private static void ReplaceAll<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record AtlasMetricCard(string Eyebrow, string Value, string Detail);

public sealed record AtlasCategoryCard(string Title, string Value, string Detail);

public sealed record AtlasVolumeCard(string Title, string Value, string Detail);

public sealed record AtlasPlanOperationCard(string Kind, string PathLine, string Detail, string Status, bool NeedsReview);

public sealed record AtlasOptimizationCard(string Kind, string Target, string Evidence, string Status, string RollbackPlan);

public sealed record AtlasUndoBatchCard(string Timestamp, string Title, string Detail);

public sealed record AtlasQuarantineCard(string Name, string OriginalPath, string Detail);

public sealed record AtlasStructureGroupCard(string Title, string Value, string Detail, string Sample);

public sealed record AtlasSignalCard(string Eyebrow, string Value, string Detail);

public sealed record AtlasActivityEntry(string Timestamp, string Title, string Detail);

public sealed record AtlasStoredMemoryCard(string Eyebrow, string Title, string Detail);
