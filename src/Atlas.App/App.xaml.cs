using Microsoft.UI.Xaml;
using Atlas.App.Services;

namespace Atlas.App;

public partial class App : Application
{
    public static App Instance => (App)Current;

    public AtlasShellSession Session { get; } = new();

    private Window? window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
        _ = Session.InitializeAsync();
    }
}
