using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Snippets.App.ViewModels;
using Snippets.App.Views;

namespace Snippets.App;

public sealed partial class MainWindow : Window
{
    private static ClipboardView? s_clipboardView;

    private readonly Dictionary<string, Control> _pages;

    public MainWindow()
    {
        InitializeComponent();

        _pages = new Dictionary<string, Control>
        {
            ["clips"] = s_clipboardView = new ClipboardView(),
            ["notes"] = CreatePage(
                "Notes",
                "Edit Markdown drafts and derive Quick Copy items from data-copy-* markers.",
                "Source editing, rendered preview, and Quick Copy panel."),
            ["jobs"] = CreatePage(
                "Jobs",
                "Manage trigger + action jobs such as clip.poll and clip.prune.",
                "Tool actions run in-process; command actions run external processes."),
            ["settings"] = CreatePage(
                "Settings",
                "Configure workspace root, clip paths, notes drafts, jobs, tray, and startup.",
                "%USERPROFILE%\\.snippets.yml"),
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

    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is FANavigationViewItem { Tag: string tag } && _pages.TryGetValue(tag, out var page))
        {
            ContentHost.Content = page;
        }
    }

    private static Control CreatePage(string title, string description, string detail)
    {
        return new Border
        {
            Padding = new Avalonia.Thickness(28),
            Child = new StackPanel
            {
                Spacing = 12,
                VerticalAlignment = VerticalAlignment.Top,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 28,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = description,
                        FontSize = 15,
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.88
                    },
                    new TextBlock
                    {
                        Text = detail,
                        FontFamily = new FontFamily("avares://Snippets.App/Assets/Fonts/MonaspaceNeon.ttf#Monaspace Neon Var"),
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.72
                    }
                }
            }
        };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
