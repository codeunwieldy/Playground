using Atlas.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atlas.App.Views;

public sealed partial class OptimizationPage : Page
{
    private AtlasShellSession Session => App.Instance.Session;

    public OptimizationPage()
    {
        InitializeComponent();
        DataContext = Session;
    }

    private async void OnAnalyzeOptimizationClick(object sender, RoutedEventArgs e) =>
        await Session.AnalyzeOptimizationAsync();
}
