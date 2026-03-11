using Atlas.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atlas.App.Views;

public sealed partial class HistoryPage : Page
{
    private AtlasShellSession Session => App.Instance.Session;

    public HistoryPage()
    {
        InitializeComponent();
        DataContext = Session;
    }

    private async void OnPreviewUndoClick(object sender, RoutedEventArgs e) =>
        await Session.PreviewUndoAsync();

    private async void OnRunScanClick(object sender, RoutedEventArgs e) =>
        await Session.RunScanAsync();
}
