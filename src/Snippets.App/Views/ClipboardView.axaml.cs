using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Snippets.App.ViewModels;

namespace Snippets.App.Views;

public partial class ClipboardView : UserControl
{
    public ClipboardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private ClipboardViewModel? ViewModel => DataContext as ClipboardViewModel;

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(ViewModel.AutoSavePath) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private static ClipCardViewModel? CardFrom(object? sender)
    {
        return (sender as Control)?.DataContext as ClipCardViewModel;
    }

    private void OnPinClick(object? sender, RoutedEventArgs e) => CardFrom(sender)?.TogglePin();

    private void OnCopyClick(object? sender, RoutedEventArgs e) => CardFrom(sender)?.CopyBack();

    private void OnRevealClick(object? sender, RoutedEventArgs e) => CardFrom(sender)?.Reveal();

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        var card = CardFrom(sender);
        if (card is null || ViewModel is null)
        {
            return;
        }

        if (!card.IsPendingDelete)
        {
            foreach (var other in ViewModel.Items)
            {
                if (!ReferenceEquals(other, card))
                {
                    other.IsPendingDelete = false;
                }
            }

            card.IsPendingDelete = true;
            return;
        }

        card.IsPendingDelete = false;
        card.Delete();
    }
}
