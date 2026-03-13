using System.ComponentModel;
using System.Numerics;
using Atlas.App.Services;
using Atlas.App.Views;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;
using Windows.Graphics;
using WinRT.Interop;

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
            "Use this space to inspect operation diffs, escalation triggers, rollback posture, and duplicate-review evidence before an execution batch is even eligible.",
            "Tighten the request, inspect the rationale, then review the diff canvas and risk summary together.",
            "Describe the outcome you want Atlas to plan, such as consolidating screenshots or cleaning duplicate documents",
            "Draft plan"),
        ["optimization"] = new(
            typeof(OptimizationPage),
            "OPTIMIZATION CENTER",
            "Tune the machine carefully without crossing into unsafe optimizer folklore.",
            "Atlas only auto-fixes curated low-risk items. Everything else stays recommendation-first with evidence, retained duplicate pressure, and rollback guidance.",
            "Optimization changes stay bounded to approved classes like temp cleanup, cache cleanup, startup clutter, and duplicate archive pressure.",
            "Ask Atlas to inspect startup clutter, temp buildup, cache pressure, duplicate installers, or safe disk cleanup opportunities",
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
            typeof(MemoryPage),
            "ATLAS MEMORY",
            "Keep the latest command understanding, safety posture, and recovery story in one readable timeline.",
            "This workspace keeps session-backed memory readable today and now carries persisted scan, drift, and duplicate-review summaries from the service.",
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
    private bool motionEnabled = true;

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
        SyncVoiceState("Voice commands stay confirmation-first and use the same risk gates as typed input.");
    }

    private void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        TryFitWindowToViewport();
        ResetShellDepth();
        motionEnabled = AreAnimationsEnabled();
        if (motionEnabled)
        {
            StartAmbientMotion();
        }
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
            SyncVoiceState("Voice commands stay confirmation-first and use the same risk gates as typed input.");
            return;
        }

        var preview = await session.PreviewVoiceIntentAsync(CommandInputBox.Text);
        session.UpdateCommandDraftPreview(preview.ParsedIntent, currentSectionTag, "Voice preview", preview.NeedsConfirmation);
        SyncVoiceState(preview.NeedsConfirmation
            ? $"Voice intent preview: {preview.ParsedIntent}. Destructive, bulk, or protected-target phrasing stays confirmation-first."
            : $"Voice intent preview: {preview.ParsedIntent}. Atlas will still route it through the same plan and policy gates as typed input.");
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
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ContentFrame.CurrentSourcePageType != section.PageType)
                {
                    ContentFrame.Navigate(section.PageType, null, new EntranceNavigationTransitionInfo());
                }
            });
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

    private void OnRootGridPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!motionEnabled)
        {
            return;
        }

        var point = e.GetCurrentPoint(RootGrid).Position;
        var width = Math.Max(1d, RootGrid.ActualWidth);
        var height = Math.Max(1d, RootGrid.ActualHeight);
        var offsetX = ((point.X / width) - 0.5d) * 18d;
        var offsetY = ((point.Y / height) - 0.5d) * 14d;

        HeroSurface.Translation = new Vector3((float)(offsetX * 0.14d), (float)(offsetY * 0.12d), 10f);
        CommandSurface.Translation = new Vector3((float)(offsetX * 0.22d), (float)(offsetY * 0.16d), 18f);
        HeroOrbHost.Translation = new Vector3((float)(offsetX * -0.42d), (float)(offsetY * -0.34d), 18f);
        CommandInsightWide.Translation = new Vector3((float)(offsetX * 0.12d), (float)(offsetY * 0.1d), 12f);
        TopGlowEllipse.Translation = new Vector3((float)(offsetX * -0.9d), (float)(offsetY * -0.7d), 0f);
        BottomGlowEllipse.Translation = new Vector3((float)(offsetX * 0.75d), (float)(offsetY * 0.65d), 0f);
        ApplyProjectionTilt(HeroSurface, offsetY * -0.16d, offsetX * 0.18d, 24d);
        ApplyProjectionTilt(CommandSurface, offsetY * -0.22d, offsetX * 0.24d, 36d);
        ApplyProjectionTilt(HeroOrbHost, offsetY * 0.28d, offsetX * -0.34d, 46d);
        ApplyProjectionTilt(CommandInsightWide, offsetY * -0.14d, offsetX * 0.16d, 18d);
    }

    private void OnRootGridPointerExited(object sender, PointerRoutedEventArgs e) =>
        ResetShellDepth();

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

    private void TryFitWindowToViewport()
    {
        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            var maxWidth = Math.Max(1100, workArea.Width - 48);
            var maxHeight = Math.Max(760, workArea.Height - 28);
            var targetWidth = Math.Min(maxWidth, Math.Max(1320, (int)(workArea.Width * 0.9d)));
            var targetHeight = Math.Min(maxHeight, Math.Max(860, (int)(workArea.Height * 0.93d)));
            var left = workArea.X + Math.Max(0, (workArea.Width - targetWidth) / 2);
            var top = workArea.Y + Math.Max(0, (workArea.Height - targetHeight) / 2);

            appWindow.MoveAndResize(new RectInt32(left, top, targetWidth, targetHeight));
        }
        catch
        {
            // Window sizing is best-effort; layout remains adaptive if this fails.
        }
    }

    private void ResetShellDepth()
    {
        HeroSurface.Translation = new Vector3(0f, 0f, 10f);
        CommandSurface.Translation = new Vector3(0f, 0f, 18f);
        HeroOrbHost.Translation = new Vector3(0f, 0f, 18f);
        CommandInsightWide.Translation = new Vector3(0f, 0f, 12f);
        TopGlowEllipse.Translation = Vector3.Zero;
        BottomGlowEllipse.Translation = Vector3.Zero;
        ApplyProjectionTilt(HeroSurface, 0d, 0d, 18d);
        ApplyProjectionTilt(CommandSurface, 0d, 0d, 28d);
        ApplyProjectionTilt(HeroOrbHost, 0d, 0d, 36d);
        ApplyProjectionTilt(CommandInsightWide, 0d, 0d, 12d);
    }

    private void StartAmbientMotion()
    {
        StartScaleLoop(OrbPulse, 1f, 1.08f, 4200d);
        StartScaleLoop(OrbCore, 1f, 1.04f, 3600d);
        StartVerticalLoop(OrbRing, -5f, 5200d);
        StartOpacityLoop(OrbHalo, 0.42f, 0.74f, 4300d);
        StartRotationLoop(OrbRing, 26000d);
        StartVerticalLoop(HeroSurface, -3f, 6200d);
        StartVerticalLoop(CommandSurface, 2f, 7000d);
        StartVerticalLoop(CommandInsightWide, -2f, 7600d);
        StartScaleLoop(TopGlowEllipse, 1f, 1.05f, 7600d);
        StartScaleLoop(BottomGlowEllipse, 1f, 1.04f, 8400d);
        StartOpacityLoop(TopGlowEllipse, 0.3f, 0.62f, 5600d);
        StartOpacityLoop(BottomGlowEllipse, 0.24f, 0.54f, 6400d);
        StartAxisScaleLoop(SignalBarOne, 1f, 1.14f, 2400d);
        StartAxisScaleLoop(SignalBarTwo, 1f, 0.88f, 2100d);
        StartAxisScaleLoop(SignalBarThree, 1f, 1.22f, 2600d);
        StartAxisScaleLoop(SignalBarFour, 1f, 0.94f, 2300d);
    }

    private static bool AreAnimationsEnabled()
    {
        try
        {
            return new UISettings().AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }

    private static void StartScaleLoop(UIElement element, float minScale, float maxScale, double durationMs)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var size = element.RenderSize;
        visual.CenterPoint = new Vector3((float)size.Width / 2f, (float)size.Height / 2f, 0f);

        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0f, new Vector3(minScale, minScale, 1f));
        animation.InsertKeyFrame(0.5f, new Vector3(maxScale, maxScale, 1f));
        animation.InsertKeyFrame(1f, new Vector3(minScale, minScale, 1f));
        animation.Duration = TimeSpan.FromMilliseconds(durationMs);
        animation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(visual.Scale), animation);
    }

    private static void StartAxisScaleLoop(UIElement element, float minYScale, float maxYScale, double durationMs)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var size = element.RenderSize;
        visual.CenterPoint = new Vector3((float)size.Width / 2f, (float)size.Height, 0f);

        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0f, new Vector3(1f, minYScale, 1f));
        animation.InsertKeyFrame(0.5f, new Vector3(1f, maxYScale, 1f));
        animation.InsertKeyFrame(1f, new Vector3(1f, minYScale, 1f));
        animation.Duration = TimeSpan.FromMilliseconds(durationMs);
        animation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(visual.Scale), animation);
    }

    private static void StartVerticalLoop(UIElement element, float peakOffset, double durationMs)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0f, Vector3.Zero);
        animation.InsertKeyFrame(0.5f, new Vector3(0f, peakOffset, 0f));
        animation.InsertKeyFrame(1f, Vector3.Zero);
        animation.Duration = TimeSpan.FromMilliseconds(durationMs);
        animation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(visual.Offset), animation);
    }

    private static void StartOpacityLoop(UIElement element, float minOpacity, float maxOpacity, double durationMs)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0f, minOpacity);
        animation.InsertKeyFrame(0.5f, maxOpacity);
        animation.InsertKeyFrame(1f, minOpacity);
        animation.Duration = TimeSpan.FromMilliseconds(durationMs);
        animation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(visual.Opacity), animation);
    }

    private static void StartRotationLoop(UIElement element, double durationMs)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var size = element.RenderSize;
        visual.CenterPoint = new Vector3((float)size.Width / 2f, (float)size.Height / 2f, 0f);

        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0f, 0f);
        animation.InsertKeyFrame(1f, 360f);
        animation.Duration = TimeSpan.FromMilliseconds(durationMs);
        animation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(visual.RotationAngleInDegrees), animation);
    }

    private void SyncVoiceState(string text)
    {
        VoiceStateText.Text = text;
        VoiceStateTextWide.Text = text;
    }

    private static void ApplyProjectionTilt(UIElement element, double rotationX, double rotationY, double depth)
    {
        if (element.Projection is not PlaneProjection projection)
        {
            return;
        }

        projection.RotationX = rotationX;
        projection.RotationY = rotationY;
        projection.GlobalOffsetZ = depth;
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
