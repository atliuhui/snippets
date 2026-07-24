using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Snippets.App.ViewModels;

namespace Snippets.App.Views;

public partial class NotesView : UserControl
{
    public NotesView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private NotesViewModel? ViewModel => DataContext as NotesViewModel;

    private async void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.CreateAsync();
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.SaveAsync();
        }
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.Refresh();
    }

    private void OnDeleteNoteClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && (sender as Control)?.DataContext is NoteListItemViewModel note)
        {
            ViewModel.Delete(note);
        }
    }

    private void OnWrapSelectionClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        try
        {
            var editor = this.FindControl<MarkdownSourceEditor>("SourceEditor");
            if (editor is null)
            {
                ViewModel.StatusText = "Could not insert Quick Copy tag: source editor is not ready.";
                return;
            }

            ViewModel.StatusText = editor.WrapSelectionWithCopyTag()
                ? "Inserted Quick Copy tag around the selection."
                : "Select text in the source editor first.";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Could not insert Quick Copy tag: {ex.Message}";
        }
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(ViewModel.DraftsPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Could not open drafts folder: {ex.Message}";
        }
    }

    private async void OnCopyQuickItemClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not QuickCopyItemViewModel item)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(item.Value);
        }
    }
}
