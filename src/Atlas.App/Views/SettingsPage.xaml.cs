using Atlas.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atlas.App.Views;

public sealed partial class SettingsPage : Page
{
    private AtlasShellSession Session => App.Instance.Session;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = Session;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        foreach (var item in RetentionComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag?.ToString() == Session.QuarantineRetentionDays.ToString())
            {
                RetentionComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private void OnRetentionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RetentionComboBox.SelectedItem is ComboBoxItem { Tag: string tag }
            && int.TryParse(tag, out var days))
        {
            Session.QuarantineRetentionDays = days;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) =>
        Session.SavePolicyChanges();

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        Session.ResetPolicy();
        OnLoaded(sender, e);
    }
}
