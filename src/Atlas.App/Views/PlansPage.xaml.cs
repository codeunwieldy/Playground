using Atlas.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atlas.App.Views;

public sealed partial class PlansPage : Page
{
    private AtlasShellSession Session => App.Instance.Session;

    public PlansPage()
    {
        InitializeComponent();
        DataContext = Session;
    }

    private async void OnPromoteCleanupPlanClick(object sender, RoutedEventArgs e) =>
        await Session.PromoteRetainedCleanupPlanAsync();

    private async void OnPreviewExecutionClick(object sender, RoutedEventArgs e) =>
        await Session.PreviewExecutionAsync();

    private async void OnExecutePlanClick(object sender, RoutedEventArgs e) =>
        await Session.ExecutePlanAsync();
}
