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

    public ObservableCollection<AtlasSignalCard> PlanSignals { get; } = new();

    public ObservableCollection<string> PlanBlockedReasons { get; } = new();

    public ObservableCollection<AtlasSignalCard> UndoSignals { get; } = new();

    public ObservableCollection<string> MutableRoots { get; } = new();

    public ObservableCollection<string> ProtectedPaths { get; } = new();

    public ObservableCollection<AtlasActivityEntry> ActivityFeed { get; } = new();

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

    public string UndoNotesText => latestCheckpoint.Notes.Count == 0
        ? "No checkpoint notes are available yet."
        : string.Join(" ", latestCheckpoint.Notes);

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

    private void SetConnectionMode(bool isLiveMode)
    {
        IsLiveMode = isLiveMode;
        if (isLiveMode)
        {
            ConnectionModeLabel = "Live service connected";
            ConnectionModeDetail = "Pipe calls are reaching the Atlas service, so scans, plans, and execution previews are using the privileged runtime.";
            RefreshSignalCollections();
            RefreshHistoryMetrics();
            return;
        }

        ConnectionModeLabel = "Preview mode";
        ConnectionModeDetail = "The shell is falling back to local read-only heuristics. Execution remains blocked until the service is reachable.";
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

        if (latestCheckpoint.InverseOperations.Count > 0 || latestCheckpoint.QuarantineItems.Count > 0)
        {
            UndoBatches.Insert(0, new AtlasUndoBatchCard(
                DateTime.Now.ToString("g"),
                $"{latestCheckpoint.InverseOperations.Count:N0} inverse ops",
                latestCheckpoint.Notes.Count > 0 ? string.Join(" ", latestCheckpoint.Notes) : "Recovery metadata is staged for the latest batch."));
        }

        ReplaceAll(QuarantineEntries, latestCheckpoint.QuarantineItems.Select(item =>
            new AtlasQuarantineCard(
                Path.GetFileName(item.OriginalPath),
                item.OriginalPath,
                item.RetentionUntilUnixTimeSeconds > 0
                    ? $"Restorable until {DateTimeOffset.FromUnixTimeSeconds(item.RetentionUntilUnixTimeSeconds).LocalDateTime:g}"
                    : "Restorable while retained in quarantine")));

        UndoSummary = HasUndoCheckpoint
            ? $"{latestCheckpoint.InverseOperations.Count:N0} inverse operations and {latestCheckpoint.QuarantineItems.Count:N0} quarantined items are available."
            : "No rollback metadata has been produced in this session yet.";

        ReplaceAll(UndoMetrics,
        [
            new AtlasMetricCard("INVERSE OPS", $"{latestCheckpoint.InverseOperations.Count:N0}", "Concrete steps Atlas can replay to reverse the latest batch."),
            new AtlasMetricCard("QUARANTINE ITEMS", $"{latestCheckpoint.QuarantineItems.Count:N0}", "Files still restorable without a full batch restore."),
            new AtlasMetricCard("CHECKPOINT NOTES", $"{latestCheckpoint.Notes.Count:N0}", "Recovery annotations staged alongside the latest undo story.")
        ]);

        RefreshUndoSignals();
        RefreshHistoryMetrics();

        OnPropertyChanged(nameof(HasUndoCheckpoint));
        OnPropertyChanged(nameof(CanPreviewUndo));
        OnPropertyChanged(nameof(CanExecuteUndo));
        OnPropertyChanged(nameof(UndoNotesText));
    }

    private void RefreshSignalCollections()
    {
        if (currentPlan.Plan.Operations.Count > 0)
        {
            RefreshPlanSignals();
        }

        if (HasUndoCheckpoint)
        {
            RefreshUndoSignals();
        }
    }

    private void RefreshHistoryMetrics()
    {
        ReplaceAll(HistoryMetrics,
        [
            new AtlasMetricCard("SESSION EVENTS", $"{ActivityFeed.Count:N0}", "Recent actions and outcomes Atlas has tracked in this shell session."),
            new AtlasMetricCard("PLANNED OPS", $"{currentPlan.Plan.Operations.Count:N0}", "Operations currently staged in the most recent plan review."),
            new AtlasMetricCard("RECOVERY ASSETS", $"{latestCheckpoint.InverseOperations.Count + latestCheckpoint.QuarantineItems.Count:N0}", "Inverse operations and direct restore items currently in memory."),
            new AtlasMetricCard("SERVICE MODE", IsLiveMode ? "Live" : "Preview", IsLiveMode
                ? "The service is reachable, so persisted history can grow beyond the current shell session."
                : "The shell is still simulating safely while the privileged service remains offline.")
        ]);
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

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var units = new[] { "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unitIndex = -1;
        do
        {
            size /= 1024d;
            unitIndex++;
        }
        while (size >= 1024d && unitIndex < units.Length - 1);

        return $"{size:0.#} {units[unitIndex]}";
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
