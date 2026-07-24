using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Snippets.App.ViewModels;
using Snippets.App.Views;

namespace Snippets.App;

public sealed partial class MainWindow : Window
{
    private static ClipboardView? s_clipboardView;
    private static NotesView? s_notesView;
    private static JobsView? s_jobsView;
    private static SettingsView? s_settingsView;

    private readonly Dictionary<string, Control> _pages;

    public MainWindow()
    {
        InitializeComponent();

        _pages = new Dictionary<string, Control>
        {
            ["clips"] = s_clipboardView = new ClipboardView(),
            ["notes"] = s_notesView = new NotesView(),
            ["jobs"] = s_jobsView = new JobsView(),
            ["settings"] = s_settingsView = new SettingsView(),
        };

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    internal static void AttachClipboardViewModel(ClipboardViewModel viewModel)
    {
        if (s_clipboardView is not null)
        {
            s_clipboardView.DataContext = viewModel;
        }
    }

    internal static void AttachJobsViewModel(JobsViewModel viewModel)
    {
        if (s_jobsView is not null)
        {
            s_jobsView.DataContext = viewModel;
        }
    }

    internal static void AttachNotesViewModel(NotesViewModel viewModel)
    {
        if (s_notesView is not null)
        {
            s_notesView.DataContext = viewModel;
        }
    }

    internal static void AttachSettingsViewModel(SettingsViewModel viewModel)
    {
        if (s_settingsView is not null)
        {
            s_settingsView.DataContext = viewModel;
        }
    }

    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is FANavigationViewItem { Tag: string tag } && _pages.TryGetValue(tag, out var page))
        {
            ContentHost.Content = page;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            if (App.ShouldCloseToTray)
            {
                Hide();
            }
            else
            {
                App.Quit();
            }

            return;
        }

        base.OnClosing(e);
    }
}
