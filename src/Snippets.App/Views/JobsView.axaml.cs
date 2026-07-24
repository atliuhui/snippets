using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Snippets.App.ViewModels;
using Snippets.Core.Config;

namespace Snippets.App.Views;

public partial class JobsView : UserControl
{
    public JobsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private static JobCardViewModel? CardFrom(object? sender)
    {
        return (sender as Control)?.DataContext as JobCardViewModel;
    }

    private void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        var card = CardFrom(sender);
        if (card is not null)
        {
            App.ToggleJob(card.Id);
        }
    }

    private async void OnRunNowClick(object? sender, RoutedEventArgs e)
    {
        var card = CardFrom(sender);
        if (card is not null)
        {
            await App.RunJobAsync(card.Id);
        }
    }

    private JobsViewModel? ViewModel => DataContext as JobsViewModel;

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await App.RefreshJobsAsync();
        }
    }

    private void OnOpenConfigFolderClick(object? sender, RoutedEventArgs e)
    {
        var configDirectory = Path.GetDirectoryName(SnippetsConfig.DefaultConfigPath());
        if (string.IsNullOrWhiteSpace(configDirectory))
        {
            if (ViewModel is not null)
            {
                ViewModel.RefreshStatusText = "Could not locate the config folder.";
            }

            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(configDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (ViewModel is not null)
            {
                ViewModel.RefreshStatusText = $"Could not open config folder: {ex.Message}";
            }
        }
    }
}
