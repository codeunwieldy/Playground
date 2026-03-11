using Atlas.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atlas.App.Views;

public sealed partial class DashboardPage : Page
{
    private AtlasShellSession Session => App.Instance.Session;

    public DashboardPage()
    {
        InitializeComponent();
        DataContext = Session;
    }

    private async void OnRunScanClick(object sender, RoutedEventArgs e) =>
        await Session.RunScanAsync();

    private async void OnAnalyzeOptimizationClick(object sender, RoutedEventArgs e) =>
        await Session.AnalyzeOptimizationAsync();
}
