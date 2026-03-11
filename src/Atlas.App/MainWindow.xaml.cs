using System.ComponentModel;
using Atlas.App.Services;
using Atlas.App.Views;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Atlas.App;

public sealed partial class MainWindow : Window
{
    private readonly AtlasShellSession session = App.Instance.Session;
    private readonly Dictionary<string, ShellSection> sections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = new(
            typeof(DashboardPage),
            "ATLAS COMMAND DECK",
            "Shape the machine without surrendering the safety rails.",
            "Ask Atlas to scan mutable roots, stage a safer organization plan, or inspect pressure before anything destructive becomes eligible.",
            "Run a scan first when you want a fresh inventory. Atlas will keep execution blocked until the service is actually present.",
            "Ask Atlas to scan mutable roots or prepare a safer organization pass",
            "Run scan"),
        ["plans"] = new(
            typeof(PlansPage),
            "PLAN REVIEW",
            "Compare the proposed structure against the current filesystem before you move a byte.",
            "Use this space to inspect operation diffs, escalation triggers, and rollback posture before an execution batch is even eligible.",
            "Tighten the request, inspect the rationale, then review the diff canvas and risk summary together.",
            "Describe the outcome you want Atlas to plan, such as consolidating screenshots or cleaning duplicate documents",
            "Draft plan"),
        ["optimization"] = new(
            typeof(OptimizationPage),
            "OPTIMIZATION CENTER",
            "Tune the machine carefully without crossing into unsafe optimizer folklore.",
            "Atlas only auto-fixes curated low-risk items. Everything else stays recommendation-first with evidence and rollback guidance.",
            "Optimization changes stay bounded to approved classes like temp cleanup, startup clutter, and duplicate installer pressure.",
            "Ask Atlas to inspect startup clutter, temp buildup, cache pressure, or safe disk cleanup opportunities",
            "Analyze system"),
        ["undo"] = new(
            typeof(UndoPage),
            "UNDO TIMELINE",
            "Every approved batch keeps a recovery story that can be inspected before you need it.",
            "Inverse operations, quarantine restores, and later checkpoint references live here so Atlas behaves like a disciplined operator instead of a one-way cleanup tool.",
            "Use the timeline to restore a single quarantined item or reason about a full batch rollback path.",
            "Ask Atlas to show the last duplicate cleanup batch or find a quarantined document to restore",
            "Preview undo"),
        ["history"] = new(
            typeof(HistoryPage),
            "ATLAS MEMORY",
            "Keep the latest command understanding, safety posture, and recovery story in one readable timeline.",
            "This workspace starts with session-backed memory today and is intentionally shaped to absorb persisted history summaries from the service next.",
            "Use Atlas Memory to understand what the shell believes happened, what safety posture is active, and which recovery artifacts are currently available.",
            "Ask Atlas to recall the latest plan, prompt trace, undo checkpoint, or session activity",
            "Preview undo"),
        ["settings"] = new(
            typeof(SettingsPage),
            "POLICY STUDIO",
            "Tune roots, exclusions, thresholds, and privacy rules without weakening the safety kernel.",
            "This is where Atlas explains the guardrails instead of hiding them. Safer defaults stay on until the user explicitly says otherwise.",
            "Policy changes should stay legible, reversible, and aligned with the hard boundary enforced by the service.",
            "Ask Atlas to explain a policy, compare scan roots, or review why sync folders stay excluded by default",
            "Save policy")
    };

    private string currentSectionTag = "dashboard";

    public MainWindow()
    {
        InitializeComponent();
        RootGrid.DataContext = session;
        TryEnableBackdrop();
        session.PropertyChanged += OnSessionPropertyChanged;
        CommandInputBox.ItemsSource = session.GetSuggestedCommands(null);
        ShellView.SelectedItem = DashboardItem;
        NavigateTo("dashboard");
        RefreshSessionStatus();
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    private async void OnQuickNavigateClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        SelectTag(tag);
        if (string.Equals(tag, "dashboard", StringComparison.OrdinalIgnoreCase))
        {
            await session.RunScanAsync();
        }
        else if (string.Equals(tag, "undo", StringComparison.OrdinalIgnoreCase))
        {
            await session.PreviewUndoAsync();
        }
    }

    private async void OnPrimaryActionClick(object sender, RoutedEventArgs e)
    {
        switch (currentSectionTag)
        {
            case "dashboard":
                await session.RunScanAsync();
                break;
            case "plans":
                await DraftPlanFromInputAsync();
                break;
            case "optimization":
                await session.AnalyzeOptimizationAsync();
                break;
            case "undo":
                await session.PreviewUndoAsync();
                break;
            case "history":
                await session.PreviewUndoAsync();
                break;
            case "settings":
                session.SavePolicyChanges();
                break;
        }
    }

    private async void OnCommandSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await DraftPlanFromInputAsync();
    }

    private void OnCommandTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.ItemsSource = session.GetSuggestedCommands(sender.Text);
        }

        session.UpdateCommandDraftPreview(sender.Text, currentSectionTag);
    }

    private void OnPromptRecipeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string prompt })
        {
            return;
        }

        CommandInputBox.Text = prompt;
        CommandInputBox.Focus(FocusState.Programmatic);

        if (prompt.Contains("optimization", StringComparison.OrdinalIgnoreCase))
        {
            SelectTag("optimization");
            session.UpdateCommandDraftPreview(prompt, "optimization", "Recipe");
            return;
        }

        SelectTag("plans");
        session.UpdateCommandDraftPreview(prompt, "plans", "Recipe");
    }

    private async void OnVoiceToggleClick(object sender, RoutedEventArgs e)
    {
        var listening = VoiceToggleButton.IsChecked == true;
        if (!listening)
        {
            session.UpdateCommandDraftPreview(CommandInputBox.Text, currentSectionTag);
            VoiceStateText.Text = "Voice commands stay confirmation-first and use the same risk gates as typed input.";
            return;
        }

        var preview = await session.PreviewVoiceIntentAsync(CommandInputBox.Text);
        session.UpdateCommandDraftPreview(preview.ParsedIntent, currentSectionTag, "Voice preview", preview.NeedsConfirmation);
        VoiceStateText.Text = preview.NeedsConfirmation
            ? $"Voice intent preview: {preview.ParsedIntent}. Confirmation will still be required."
            : $"Voice intent preview: {preview.ParsedIntent}.";
    }

    private async Task DraftPlanFromInputAsync()
    {
        if (string.IsNullOrWhiteSpace(CommandInputBox.Text))
        {
            CommandInputBox.Text = "Organize Downloads into cleaner categories, review duplicate files, and keep every destructive step reversible.";
        }

        SelectTag("plans");
        await session.DraftPlanAsync(CommandInputBox.Text);
        CommandInputBox.Focus(FocusState.Programmatic);
    }

    private void SelectTag(string tag)
    {
        var target = GetNavigationItem(tag);
        if (target is not null && !ReferenceEquals(ShellView.SelectedItem, target))
        {
            ShellView.SelectedItem = target;
        }

        NavigateTo(tag);
    }

    private void NavigateTo(string tag)
    {
        if (!sections.TryGetValue(tag, out var section))
        {
            return;
        }

        currentSectionTag = tag;
        if (ContentFrame.CurrentSourcePageType != section.PageType)
        {
            ContentFrame.Navigate(section.PageType, null, new EntranceNavigationTransitionInfo());
        }

        ShellEyebrowText.Text = section.Eyebrow;
        ShellTitleText.Text = section.Title;
        ShellSubtitleText.Text = section.Description;
        ShellHintText.Text = section.Hint;
        CommandInputBox.PlaceholderText = section.Placeholder;
        PrimaryActionButton.Content = section.PrimaryAction;
        VoiceToggleButton.Content = tag == "plans" ? "Confirm by voice" : "Push to talk";
        session.UpdateCommandDraftPreview(CommandInputBox.Text, currentSectionTag);
    }

    private void RefreshSessionStatus()
    {
        StatusEyebrowText.Text = session.IsLiveMode ? "LIVE SERVICE" : "PREVIEW MODE";
        StatusBadgeText.Text = session.ConnectionModeLabel;
        StatusDetailText.Text = session.ConnectionModeDetail;
        SessionStatusText.Text = session.BusyStateLabel;
        BusyProgressBar.IsIndeterminate = session.IsBusy;
        BusyProgressBar.Visibility = session.IsBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        RefreshSessionStatus();

    private NavigationViewItem? GetNavigationItem(string tag) =>
        tag switch
        {
            "dashboard" => DashboardItem,
            "plans" => PlansItem,
            "optimization" => OptimizationItem,
            "undo" => UndoItem,
            "history" => HistoryItem,
            "settings" => SettingsItem,
            _ => null
        };

    private void TryEnableBackdrop()
    {
        try
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
        catch
        {
            // Backdrop support is optional in local verification environments.
        }
    }

    private sealed record ShellSection(
        Type PageType,
        string Eyebrow,
        string Title,
        string Description,
        string Hint,
        string Placeholder,
        string PrimaryAction);
}
