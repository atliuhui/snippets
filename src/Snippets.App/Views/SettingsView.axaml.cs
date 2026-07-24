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
            await ViewModel.SetTrayEnabledAsync(toggle.IsChecked == true);
        }
    }
}
