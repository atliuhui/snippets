using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Snippets.App.Services;
using Snippets.App.ViewModels;
using Snippets.Core.Clips;
using Snippets.Core.Config;

namespace Snippets.App;

public sealed partial class App : Application
{
    public static bool IsShuttingDown { get; private set; }

    private static ClipStore? s_clipStore;
    private static ClipboardWatcher? s_clipWatcher;

    public static ClipboardViewModel? ClipboardViewModel { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            InitClipCore();
            var appIcon = LoadAppIcon();

            var window = new MainWindow { Icon = appIcon };
            window.Opened += (_, _) => StartClipboardWatcher(window.Clipboard);
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ToggleClipboardWatcher(bool running)
    {
        if (s_clipWatcher is null)
        {
            return;
        }

        if (running)
        {
            s_clipWatcher.Start();
        }
        else
        {
            s_clipWatcher.Stop();
        }
    }

    private static void InitClipCore()
    {
        var config = SnippetsConfig.CreateDefault();
        s_clipStore = new ClipStore(new ClipStoreOptions(
            config.Clips.AutoSave,
            config.Clips.Favorites,
            config.Clips.MaxAutoSave));
        s_clipStore.PruneAutoSave();
    }

    private static void StartClipboardWatcher(IClipboard? clipboard)
    {
        if (clipboard is null || s_clipStore is null || s_clipWatcher is not null)
        {
            return;
        }

        ClipboardViewModel = new ClipboardViewModel(s_clipStore, clipboard);
        s_clipWatcher = new ClipboardWatcher(clipboard, s_clipStore);
        s_clipWatcher.ItemSaved += item =>
            Dispatcher.UIThread.Post(() => ClipboardViewModel?.OnItemSaved(item));
        s_clipWatcher.Start();

        MainWindow.AttachClipboardViewModel(ClipboardViewModel);
    }

    public static void Quit()
    {
        IsShuttingDown = true;
        s_clipWatcher?.Dispose();
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private static WindowIcon LoadAppIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Snippets.App/Assets/app.ico"));
        return new WindowIcon(stream);
    }
}
