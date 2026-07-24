using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Snippets.App.ViewModels;

namespace Snippets.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void OnOpenConfigFolderClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(ViewModel.ConfigPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            ViewModel.Fail("Cannot open the config folder because the config path is invalid.");
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ViewModel.Fail(ex.Message);
        }
    }

    private async void OnStartWithSystemClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && sender is ToggleSwitch toggle)
        {
            await ViewModel.SetStartWithSystemEnabledAsync(toggle.IsChecked == true);
        }
    }

    private async void OnTrayClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && sender is ToggleSwitch toggle)
        {
            await ViewModel.SetCloseToTrayEnabledAsync(toggle.IsChecked == true);
        }
    }
}
