using Atlas.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atlas.App.Views;

public sealed partial class MemoryPage : Page
{
    private AtlasShellSession Session => App.Instance.Session;

    public MemoryPage()
    {
        InitializeComponent();
        DataContext = Session;
    }

    private async void OnRefreshMemoryClick(object sender, RoutedEventArgs e) =>
        await Session.RefreshHistoryAsync();

    private async void OnRunScanClick(object sender, RoutedEventArgs e) =>
        await Session.RunScanAsync();

    private async void OnPreviewUndoClick(object sender, RoutedEventArgs e) =>
        await Session.PreviewUndoAsync();
}
