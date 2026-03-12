using System.Collections.Specialized;
using System.Linq;
using Atlas.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace Atlas.App.Views;

public sealed class HistoryPage : Page
{
    private readonly StackPanel historyMetricsHost = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 16
    };

    private readonly StackPanel inventoryMemoryHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel scanContinuityHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel rescanStoryHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel driftReviewHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel scanPairHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel driftFileSampleHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel driftHotspotHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel scanProvenanceHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel persistedScanSessionHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel persistedVolumeHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel persistedFileSampleHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel planSignalsHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel undoSignalsHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel activityFeedHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel undoBatchesHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel persistedPlanHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel persistedFindingHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel persistedTraceHost = new()
    {
        Spacing = 12
    };

    private readonly StackPanel quarantineHost = new()
    {
        Spacing = 12
    };

    private AtlasShellSession Session => App.Instance.Session;

    public HistoryPage()
    {
        DataContext = Session;
        AttachCollectionListeners();
        Content = BuildLayout();
        RefreshDynamicSections();
        Unloaded += OnUnloaded;
    }

    private UIElement BuildLayout()
    {
        var layout = new StackPanel
        {
            Spacing = 20,
            Padding = new Thickness(28, 22, 28, 32)
        };

        layout.Children.Add(CreateHeroCard());
        layout.Children.Add(CreateMetricsSection());
        layout.Children.Add(CreateInventoryMemoryCard());
        layout.Children.Add(CreateScanContinuityCard());
        layout.Children.Add(CreateRescanStoryCard());
        layout.Children.Add(CreateDriftReviewCard());
        layout.Children.Add(CreateTwoColumnRow(CreateScanPairCard(), CreateDriftFileSampleCard(), 430, 430));
        layout.Children.Add(CreateDriftHotspotCard());
        layout.Children.Add(CreateScanProvenanceCard());
        layout.Children.Add(CreateTwoColumnRow(CreateStoredScansCard(), CreateStoredVolumesCard(), 430, 430));
        layout.Children.Add(CreateStoredFileSampleCard());
        layout.Children.Add(CreateTwoColumnRow(CreateStoredPlansCard(), CreateStoredFindingsCard(), 430, 430));
        layout.Children.Add(CreateTwoColumnRow(CreateIntentCard(), CreateServiceCard(), 480, 360));
        layout.Children.Add(CreateTwoColumnRow(CreatePlanCard(), CreateUndoCard(), 420, 420));
        layout.Children.Add(CreateTwoColumnRow(CreatePlanSignalsCard(), CreateUndoSignalsCard(), 420, 420));
        layout.Children.Add(CreateTraceMemoryCard());
        layout.Children.Add(CreateTwoColumnRow(CreateActivityCard(), CreateUndoBatchesCard(), 520, 360));
        layout.Children.Add(CreateQuarantineCard());

        return new ScrollViewer
        {
            Content = layout
        };
    }

    private void AttachCollectionListeners()
    {
        Session.HistoryMetrics.CollectionChanged += OnDynamicCollectionChanged;
        Session.InventoryMemoryCards.CollectionChanged += OnDynamicCollectionChanged;
        Session.ScanContinuitySignals.CollectionChanged += OnDynamicCollectionChanged;
        Session.RescanStoryCards.CollectionChanged += OnDynamicCollectionChanged;
        Session.DriftReviewCards.CollectionChanged += OnDynamicCollectionChanged;
        Session.ScanPairSignals.CollectionChanged += OnDynamicCollectionChanged;
        Session.DriftFileSampleCards.CollectionChanged += OnDynamicCollectionChanged;
        Session.DriftHotspotCards.CollectionChanged += OnDynamicCollectionChanged;
        Session.ScanProvenanceSignals.CollectionChanged += OnDynamicCollectionChanged;
        Session.PersistedScanSessionMemory.CollectionChanged += OnDynamicCollectionChanged;
        Session.PersistedVolumeMemory.CollectionChanged += OnDynamicCollectionChanged;
        Session.PersistedFileSampleMemory.CollectionChanged += OnDynamicCollectionChanged;
        Session.PlanSignals.CollectionChanged += OnDynamicCollectionChanged;
        Session.UndoSignals.CollectionChanged += OnDynamicCollectionChanged;
        Session.ActivityFeed.CollectionChanged += OnDynamicCollectionChanged;
        Session.UndoBatches.CollectionChanged += OnDynamicCollectionChanged;
        Session.QuarantineEntries.CollectionChanged += OnDynamicCollectionChanged;
        Session.PersistedPlanMemory.CollectionChanged += OnDynamicCollectionChanged;
        Session.PersistedFindingMemory.CollectionChanged += OnDynamicCollectionChanged;
        Session.PersistedTraceMemory.CollectionChanged += OnDynamicCollectionChanged;
    }

    private void DetachCollectionListeners()
    {
        Session.HistoryMetrics.CollectionChanged -= OnDynamicCollectionChanged;
        Session.InventoryMemoryCards.CollectionChanged -= OnDynamicCollectionChanged;
        Session.ScanContinuitySignals.CollectionChanged -= OnDynamicCollectionChanged;
        Session.RescanStoryCards.CollectionChanged -= OnDynamicCollectionChanged;
        Session.DriftReviewCards.CollectionChanged -= OnDynamicCollectionChanged;
        Session.ScanPairSignals.CollectionChanged -= OnDynamicCollectionChanged;
        Session.DriftFileSampleCards.CollectionChanged -= OnDynamicCollectionChanged;
        Session.DriftHotspotCards.CollectionChanged -= OnDynamicCollectionChanged;
        Session.ScanProvenanceSignals.CollectionChanged -= OnDynamicCollectionChanged;
        Session.PersistedScanSessionMemory.CollectionChanged -= OnDynamicCollectionChanged;
        Session.PersistedVolumeMemory.CollectionChanged -= OnDynamicCollectionChanged;
        Session.PersistedFileSampleMemory.CollectionChanged -= OnDynamicCollectionChanged;
        Session.PlanSignals.CollectionChanged -= OnDynamicCollectionChanged;
        Session.UndoSignals.CollectionChanged -= OnDynamicCollectionChanged;
        Session.ActivityFeed.CollectionChanged -= OnDynamicCollectionChanged;
        Session.UndoBatches.CollectionChanged -= OnDynamicCollectionChanged;
        Session.QuarantineEntries.CollectionChanged -= OnDynamicCollectionChanged;
        Session.PersistedPlanMemory.CollectionChanged -= OnDynamicCollectionChanged;
        Session.PersistedFindingMemory.CollectionChanged -= OnDynamicCollectionChanged;
        Session.PersistedTraceMemory.CollectionChanged -= OnDynamicCollectionChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachCollectionListeners();
        Unloaded -= OnUnloaded;
    }

    private void OnDynamicCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshDynamicSections();

    private void RefreshDynamicSections()
    {
        ReplaceChildren(historyMetricsHost, Session.HistoryMetrics.Select(CreateMetricTile));
        ReplaceChildren(inventoryMemoryHost, Session.InventoryMemoryCards.Select(CreateStoredMemoryTile));
        ReplaceChildren(scanContinuityHost, Session.ScanContinuitySignals.Select(CreateSignalTile));
        ReplaceChildren(rescanStoryHost, Session.RescanStoryCards.Select(CreateStoredMemoryTile));
        ReplaceChildren(driftReviewHost, Session.DriftReviewCards.Select(CreateStoredMemoryTile));
        ReplaceChildren(scanPairHost, Session.ScanPairSignals.Select(CreateSignalTile));
        RefreshDriftFileSampleSection();
        ReplaceChildren(driftHotspotHost, Session.DriftHotspotCards.Select(CreateStoredMemoryTile));
        ReplaceChildren(scanProvenanceHost, Session.ScanProvenanceSignals.Select(CreateSignalTile));
        ReplaceChildren(persistedScanSessionHost, Session.PersistedScanSessionMemory.Select(CreateStoredMemoryTile));
        ReplaceChildren(persistedVolumeHost, Session.PersistedVolumeMemory.Select(CreateStoredMemoryTile));
        ReplaceChildren(persistedFileSampleHost, Session.PersistedFileSampleMemory.Select(CreateStoredMemoryTile));
        ReplaceChildren(planSignalsHost, Session.PlanSignals.Select(CreateSignalTile));
        ReplaceChildren(undoSignalsHost, Session.UndoSignals.Select(CreateSignalTile));
        ReplaceChildren(activityFeedHost, Session.ActivityFeed.Select(CreateActivityTile));
        ReplaceChildren(undoBatchesHost, Session.UndoBatches.Select(CreateUndoBatchTile));
        ReplaceChildren(persistedPlanHost, Session.PersistedPlanMemory.Select(CreateStoredMemoryTile));
        ReplaceChildren(persistedFindingHost, Session.PersistedFindingMemory.Select(CreateStoredMemoryTile));
        ReplaceChildren(persistedTraceHost, Session.PersistedTraceMemory.Select(CreateStoredMemoryTile));
        ReplaceChildren(quarantineHost, Session.QuarantineEntries.Select(CreateQuarantineTile));
    }

    private void RefreshDriftFileSampleSection()
    {
        driftFileSampleHost.Children.Clear();
        foreach (var card in Session.DriftFileSampleCards)
        {
            var tile = CreateStoredMemoryTile(card);
            if (card.Eyebrow.EndsWith("FILES", StringComparison.Ordinal))
            {
                tile.Margin = new Thickness(0, 8, 0, 0);
            }
            driftFileSampleHost.Children.Add(tile);
        }
    }

    private FrameworkElement CreateHeroCard()
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        actions.Children.Add(CreateButton("Preview undo", "AtlasSecondaryButtonStyle", OnPreviewUndoClick, nameof(AtlasShellSession.CanPreviewUndo)));
        actions.Children.Add(CreateButton("Refresh memory", "AtlasSecondaryButtonStyle", OnRefreshMemoryClick));
        actions.Children.Add(CreateButton("Refresh scan", "AtlasPrimaryButtonStyle", OnRunScanClick));

        return CreateCard(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                CreateText("ATLAS MEMORY", "AtlasEyebrowStyle"),
                CreateText("Command context, safety posture, and recovery state", "AtlasSectionHeadingStyle"),
                CreateWrappedText("This workspace keeps the current session readable today and is intentionally shaped to absorb persisted scan, plan, undo, optimization, quarantine, and prompt-trace history as the service grows.", "AtlasBodyTextStyle"),
                CreateBoundText(nameof(AtlasShellSession.PersistedMemorySummaryText), "AtlasBodyTextStyle"),
                actions
            }
        });
    }

    private FrameworkElement CreateMetricsSection()
    {
        return CreateCard(new StackPanel
        {
            Spacing = 16,
            Children =
            {
                CreateText("SESSION SNAPSHOT", "AtlasEyebrowStyle"),
                CreateText("How much Atlas currently remembers", "AtlasSectionHeadingStyle"),
                new ScrollViewer
                {
                    HorizontalScrollMode = ScrollMode.Enabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollMode = ScrollMode.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = historyMetricsHost
                }
            }
        }, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateInventoryMemoryCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("SCAN MEMORY", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Current and stored inventory posture", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.InventoryMemorySummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(inventoryMemoryHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateScanContinuityCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("SCAN CONTINUITY", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Cadence and drift across stored sessions", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.ScanContinuitySummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(scanContinuityHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateRescanStoryCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("RESCAN STORY", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("How the retained session window reads as a timeline", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.RescanStorySummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(rescanStoryHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateDriftReviewCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("DRIFT REVIEW", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("What changed across stored scan pairs", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.DriftReviewSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(driftReviewHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateScanPairCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("SCAN PAIR", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("How Atlas frames the latest stored comparison window", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.ScanPairSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(scanPairHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateDriftFileSampleCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("CHANGED PATH SAMPLE", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Bounded file evidence from the latest stored drift window", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.DriftFileSampleSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(driftFileSampleHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateDriftHotspotCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("DRIFT HOTSPOTS", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("What kinds of files are shifting most in the latest drift window", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.DriftHotspotSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(driftHotspotHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateScanProvenanceCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("SCAN PROVENANCE", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("How Atlas explains session origin and evidence depth", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.ScanProvenanceSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(scanProvenanceHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateStoredScansCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("STORED SCANS", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Recent persisted inventory sessions", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.PersistedScanSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(persistedScanSessionHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateStoredVolumesCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("STORED VOLUMES", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Latest persisted volume posture", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.PersistedVolumeSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(persistedVolumeHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateStoredFileSampleCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("STORED FILE SAMPLE", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Latest persisted file rows", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.PersistedFileSampleSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(persistedFileSampleHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateIntentCard()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(CreateText("LATEST INTENT", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.CommandInterpretationText), "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.CommandInterpretationDetailText), "AtlasBodyTextStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.CommandReviewLabel), "AtlasEyebrowStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.CommandReviewDetailText), "AtlasBodyTextStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.CommandNextStepLabel), "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.CommandNextStepDetailText), "AtlasBodyTextStyle"));
        return CreateCard(stack);
    }

    private FrameworkElement CreateStoredPlansCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("STORED PLANS", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Persisted organization memory", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.PersistedPlanSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(persistedPlanHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateStoredFindingsCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("STORED FINDINGS", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Persisted optimization evidence", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.PersistedFindingSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(persistedFindingHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateServiceCard()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(CreateText("SERVICE POSTURE", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.ConnectionModeLabel), "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.ConnectionModeDetail), "AtlasBodyTextStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.BusyStateLabel), "AtlasBodyTextStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.CurrentFocus), "AtlasBodyTextStyle"));
        return CreateCard(stack);
    }

    private FrameworkElement CreatePlanCard()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(CreateText("PLAN MEMORY", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.PlanSummary), "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.RiskSummary), "AtlasBodyTextStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.PlanSignalSummaryText), "AtlasBodyTextStyle"));
        return CreateCard(stack);
    }

    private FrameworkElement CreateUndoCard()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(CreateText("RECOVERY MEMORY", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.UndoSummary), "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.UndoNotesText), "AtlasBodyTextStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.UndoSignalSummaryText), "AtlasBodyTextStyle"));
        return CreateCard(stack);
    }

    private FrameworkElement CreatePlanSignalsCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("PLAN SIGNALS", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Approval and risk posture", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.PlanSignalSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(planSignalsHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateUndoSignalsCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("RECOVERY SIGNALS", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Restore depth and checkpoint strength", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.UndoSignalSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(undoSignalsHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateActivityCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("RECENT ACTIVITY", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("What Atlas has done in this shell session", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.RecentActivitySummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(activityFeedHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateTraceMemoryCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("PROMPT TRACES", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Planning and voice parsing memory", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.PersistedTraceSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(persistedTraceHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private FrameworkElement CreateUndoBatchesCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("UNDO BATCHES", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Latest rollback stories", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.UndoSummary), "AtlasBodyTextStyle"));
        stack.Children.Add(undoBatchesHost);
        return CreateCard(stack);
    }

    private FrameworkElement CreateQuarantineCard()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(CreateText("QUARANTINE", "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText("Restore-ready items", "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateBoundText(nameof(AtlasShellSession.QuarantineSummaryText), "AtlasBodyTextStyle"));
        stack.Children.Add(quarantineHost);
        return CreateCard(stack, "AtlasPanelStyle", new Thickness(24));
    }

    private UIElement CreateTwoColumnRow(FrameworkElement left, FrameworkElement right, double leftWidth, double rightWidth)
    {
        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rightWidth) });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = grid
        };
    }

    private FrameworkElement CreateMetricTile(AtlasMetricCard metric)
    {
        var stack = new StackPanel
        {
            Spacing = 8
        };
        stack.Children.Add(CreateText(metric.Eyebrow, "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText(metric.Value, "AtlasMetricValueStyle"));
        stack.Children.Add(CreateText(metric.Detail, "AtlasBodyTextStyle"));

        return CreateCard(stack, "AtlasCardStyle", new Thickness(18));
    }

    private FrameworkElement CreateSignalTile(AtlasSignalCard signal)
    {
        var stack = new StackPanel
        {
            Spacing = 8
        };
        stack.Children.Add(CreateText(signal.Eyebrow, "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText(signal.Value, "AtlasSignalValueStyle"));
        stack.Children.Add(CreateText(signal.Detail, "AtlasBodyTextStyle"));

        return CreateCard(stack, "AtlasCardStyle", new Thickness(18));
    }

    private FrameworkElement CreateActivityTile(AtlasActivityEntry entry)
    {
        var stack = new StackPanel
        {
            Spacing = 6
        };
        stack.Children.Add(CreateText(entry.Timestamp, "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText(entry.Title, "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateText(entry.Detail, "AtlasBodyTextStyle"));

        return CreateCard(stack, "AtlasCardStyle", new Thickness(18));
    }

    private FrameworkElement CreateUndoBatchTile(AtlasUndoBatchCard batch)
    {
        var stack = new StackPanel
        {
            Spacing = 6
        };
        stack.Children.Add(CreateText(batch.Timestamp, "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText(batch.Title, "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateText(batch.Detail, "AtlasBodyTextStyle"));

        return CreateCard(stack, "AtlasCardStyle", new Thickness(18));
    }

    private FrameworkElement CreateQuarantineTile(AtlasQuarantineCard item)
    {
        var stack = new StackPanel
        {
            Spacing = 6
        };
        stack.Children.Add(CreateText(item.Name, "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateText(item.OriginalPath, "AtlasBodyTextStyle"));
        stack.Children.Add(CreateText(item.Detail, "AtlasBodyTextStyle"));

        return CreateCard(stack, "AtlasCardStyle", new Thickness(18));
    }

    private FrameworkElement CreateStoredMemoryTile(AtlasStoredMemoryCard item)
    {
        var stack = new StackPanel
        {
            Spacing = 6
        };
        stack.Children.Add(CreateText(item.Eyebrow, "AtlasEyebrowStyle"));
        stack.Children.Add(CreateText(item.Title, "AtlasSectionHeadingStyle"));
        stack.Children.Add(CreateText(item.Detail, "AtlasBodyTextStyle"));

        return CreateCard(stack, "AtlasCardStyle", new Thickness(18));
    }

    private Border CreateCard(UIElement content, string styleKey = "AtlasCardStyle", Thickness? padding = null)
    {
        return new Border
        {
            Style = ResolveStyle(styleKey),
            Padding = padding ?? new Thickness(20),
            Child = content
        };
    }

    private Button CreateButton(string content, string styleKey, RoutedEventHandler handler, string? isEnabledPath = null)
    {
        var button = new Button
        {
            Content = content,
            Style = ResolveStyle(styleKey)
        };

        button.Click += handler;

        if (!string.IsNullOrWhiteSpace(isEnabledPath))
        {
            button.SetBinding(IsEnabledProperty, CreateBinding(isEnabledPath));
        }

        return button;
    }

    private TextBlock CreateText(string text, string styleKey)
    {
        return new TextBlock
        {
            Text = text,
            Style = ResolveStyle(styleKey),
            TextWrapping = TextWrapping.Wrap
        };
    }

    private TextBlock CreateWrappedText(string text, string styleKey) => CreateText(text, styleKey);

    private TextBlock CreateBoundText(string path, string styleKey)
    {
        var textBlock = new TextBlock
        {
            Style = ResolveStyle(styleKey),
            TextWrapping = TextWrapping.Wrap
        };
        textBlock.SetBinding(TextBlock.TextProperty, CreateBinding(path));
        return textBlock;
    }

    private static Binding CreateBinding(string path) =>
        new()
        {
            Path = new PropertyPath(path),
            Mode = BindingMode.OneWay
        };

    private static void ReplaceChildren(Panel panel, IEnumerable<FrameworkElement> items)
    {
        panel.Children.Clear();
        foreach (var item in items)
        {
            panel.Children.Add(item);
        }
    }

    private static Style? ResolveStyle(string key) => Application.Current.Resources[key] as Style;

    private async void OnPreviewUndoClick(object sender, RoutedEventArgs e) =>
        await Session.PreviewUndoAsync();

    private async void OnRunScanClick(object sender, RoutedEventArgs e) =>
        await Session.RunScanAsync();

    private async void OnRefreshMemoryClick(object sender, RoutedEventArgs e) =>
        await Session.RefreshHistoryAsync();
}
