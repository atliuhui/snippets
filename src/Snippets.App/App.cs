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
using Snippets.Core.Jobs;
using Snippets.Core.Notes;

namespace Snippets.App;

public sealed partial class App : Application
{
    private const string TrayQuickNoteName = "quick.md";

    public static bool IsShuttingDown { get; private set; }

    private static ClipStore? s_clipStore;
    private static NoteService? s_noteService;
    private static ClipboardWatcher? s_clipWatcher;
    private static SnippetsConfig? s_config;
    private static JobScheduler? s_jobScheduler;
    private static MainWindow? s_mainWindow;
    private static TrayIcon? s_trayIcon;
    private static NativeMenu? s_trayQuickMenu;

    public static ClipboardViewModel? ClipboardViewModel { get; private set; }
    public static NotesViewModel? NotesViewModel { get; private set; }
    public static JobsViewModel? JobsViewModel { get; private set; }
    public static SettingsViewModel? SettingsViewModel { get; private set; }

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
            s_mainWindow = window;
            if (JobsViewModel is not null)
            {
                MainWindow.AttachJobsViewModel(JobsViewModel);
            }

            if (NotesViewModel is not null)
            {
                MainWindow.AttachNotesViewModel(NotesViewModel);
            }

            if (SettingsViewModel is not null)
            {
                MainWindow.AttachSettingsViewModel(SettingsViewModel);
            }

            window.Opened += (_, _) => StartClipboardWatcher(window.Clipboard);
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            EnsureTrayIcon();
            StartupService.SetStartWithSystem(s_config?.App.StartWithSystem == true);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ToggleJob(string jobId)
    {
        if (s_jobScheduler is null)
        {
            return;
        }

        if (s_jobScheduler.IsJobRunning(jobId))
        {
            s_jobScheduler.StopJob(jobId);
            if (jobId == "clip-poll")
            {
                s_clipWatcher?.Stop();
            }
        }
        else
        {
            if (jobId == "clip-poll")
            {
                s_clipWatcher?.Start();
            }

            s_jobScheduler.StartScheduledJob(jobId);
        }

        JobsViewModel?.SetJobRunning(jobId, s_jobScheduler.IsJobRunning(jobId));
    }

    public static async Task RunJobAsync(string jobId)
    {
        if (s_jobScheduler is null)
        {
            return;
        }

        JobsViewModel?.SetJobBusy(jobId, true);
        try
        {
            await s_jobScheduler.RunManualAsync(jobId);
        }
        finally
        {
            JobsViewModel?.SetJobBusy(jobId, s_jobScheduler.IsJobExecuting(jobId));
        }
    }

    public static async Task RefreshJobsAsync()
    {
        if (JobsViewModel is null || s_jobScheduler is null)
        {
            return;
        }

        if (s_jobScheduler.HasActiveRuns)
        {
            JobsViewModel.RefreshStatusText = "Cannot refresh while a job is running.";
            return;
        }

        try
        {
            s_config = SnippetsConfig.Load();
            JobsViewModel.ReplaceJobs(s_config.Jobs);
            RebuildTrayQuickMenu();
            await RestartJobsAsync();
            JobsViewModel.RefreshStatusText = $"Reloaded {s_config.Jobs.Items.Count} jobs from {SnippetsConfig.DefaultConfigPath()}.";
        }
        catch (Exception ex)
        {
            JobsViewModel.RefreshStatusText = ex.Message;
        }
    }

    public static bool ShouldCloseToTray => s_config?.App.CloseToTray == true;

    public static Task<SnippetsConfig> UpdateAppSettingsAsync(bool closeToTray, bool startWithSystem)
    {
        StartupService.SetStartWithSystem(startWithSystem);
        var config = SnippetsConfig.SaveAppSettings(closeToTray, startWithSystem);
        s_config = config;
        EnsureTrayIcon();
        return Task.FromResult(config);
    }

    private static void InitClipCore()
    {
        s_config = SnippetsConfig.Load();
        s_clipStore = new ClipStore(new ClipStoreOptions(
            s_config.Clips.AutoSave,
            s_config.Clips.Favorites,
            s_config.Clips.MaxAutoSave));
        s_noteService = new NoteService(s_config.Notes.Drafts);
        NotesViewModel = new NotesViewModel(s_noteService);
        NotesViewModel.DraftsChanged += () => Dispatcher.UIThread.Post(RebuildTrayQuickMenu);
        JobsViewModel = new JobsViewModel(s_config.Jobs);
        SettingsViewModel = new SettingsViewModel(s_config, SnippetsConfig.DefaultConfigPath());
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
        _ = RestartJobsAsync();

        MainWindow.AttachClipboardViewModel(ClipboardViewModel);
    }

    private static async Task RestartJobsAsync()
    {
        if (s_config is null || s_clipStore is null || s_clipWatcher is null)
        {
            throw new InvalidOperationException("Clip services must be initialized before jobs can start.");
        }

        var tools = new ToolRegistry();
        tools.Register("clip.poll", async (_, cancellationToken) =>
        {
            await Dispatcher.UIThread.InvokeAsync(s_clipWatcher.PollAsync, DispatcherPriority.Background, cancellationToken);
            return null;
        });
        tools.Register("clip.prune", (args, _) =>
        {
            var deleted = s_clipStore.PruneAutoSave(ReadMaxAutoSave(args));
            return Task.FromResult<string?>($"deleted={deleted}");
        });

        s_jobScheduler?.Dispose();
        s_jobScheduler = new JobScheduler(new JobRunner(tools));
        s_jobScheduler.JobStarted += job =>
            Dispatcher.UIThread.Post(() => JobsViewModel?.SetJobBusy(job.Id, true));
        s_jobScheduler.JobCompleted += record =>
            Dispatcher.UIThread.Post(() =>
            {
                JobsViewModel?.OnJobCompleted(record);
                JobsViewModel?.SetJobBusy(record.Job.Id, s_jobScheduler?.IsJobExecuting(record.Job.Id) == true);
            });
        await s_jobScheduler.StartAsync(s_config.Jobs);
        foreach (var job in s_config.Jobs.Items.Where(job => job.Trigger.Type is "interval" or "cron"))
        {
            JobsViewModel?.SetJobRunning(job.Id, s_jobScheduler.IsJobRunning(job.Id));
        }
    }

    private static int? ReadMaxAutoSave(IReadOnlyDictionary<string, string> args)
    {
        if (!args.TryGetValue("maxAutoSave", out var raw))
        {
            return null;
        }

        return int.TryParse(raw, out var maxAutoSave) && maxAutoSave > 0
            ? maxAutoSave
            : throw new InvalidOperationException("clip.prune maxAutoSave must be a positive integer.");
    }

    public static void Quit()
    {
        IsShuttingDown = true;
        s_trayIcon?.Dispose();
        s_trayIcon = null;
        s_trayQuickMenu = null;
        s_jobScheduler?.Dispose();
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

    private static void EnsureTrayIcon()
    {
        if (s_trayIcon is not null)
        {
            s_trayIcon.IsVisible = true;
            RebuildTrayQuickMenu();
            return;
        }

        var openItem = new NativeMenuItem("Open");
        openItem.Click += (_, _) => ShowMainWindow();
        s_trayQuickMenu = new NativeMenu();
        var quickItem = new NativeMenuItem("Quick") { Menu = s_trayQuickMenu };
        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => Quit();

        s_trayIcon = new TrayIcon
        {
            Icon = LoadAppIcon(),
            ToolTipText = "Snippets",
            IsVisible = true,
            Menu = new NativeMenu
            {
                Items =
                {
                    openItem,
                    quickItem,
                    new NativeMenuItemSeparator(),
                    quitItem
                }
            }
        };
        s_trayIcon.Clicked += (_, _) => ShowMainWindow();
        RebuildTrayQuickMenu();
    }

    private static void RebuildTrayQuickMenu()
    {
        if (s_trayQuickMenu is null)
        {
            return;
        }

        s_trayQuickMenu.Items.Clear();
        var quickItems = LoadTrayQuickItems();
        var limit = s_config?.App.TrayQuickLimit ?? AppConfig.DefaultTrayQuickLimit;
        if (quickItems.Length == 0)
        {
            s_trayQuickMenu.Items.Add(new NativeMenuItem("No items in quick.md") { IsEnabled = false });
            return;
        }

        foreach (var item in quickItems.Take(limit))
        {
            var value = item.Value;
            var menuItem = new NativeMenuItem(FormatTrayQuickLabel(item));
            menuItem.Click += (_, _) => _ = CopyTrayQuickValueAsync(value);
            s_trayQuickMenu.Items.Add(menuItem);
        }

        if (quickItems.Length > limit)
        {
            s_trayQuickMenu.Items.Add(new NativeMenuItemSeparator());
            s_trayQuickMenu.Items.Add(new NativeMenuItem($"Open quick.md for {quickItems.Length - limit} more") { IsEnabled = false });
        }
    }

    private static QuickCopyItem[] LoadTrayQuickItems()
    {
        if (s_noteService is null)
        {
            return [];
        }

        var document = s_noteService.TryRead(TrayQuickNoteName);
        if (document is null)
        {
            return [];
        }

        return new QuickCopyExtractor()
            .Extract(document.Content, document.Path, document.Updated)
            .Items
            .ToArray();
    }

    private static async Task CopyTrayQuickValueAsync(string value)
    {
        var clipboard = s_mainWindow?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(value);
        }
    }

    private static string FormatTrayQuickLabel(QuickCopyItem item)
    {
        var label = string.IsNullOrWhiteSpace(item.Label) ? item.Id : item.Label;
        return label.Length <= 48 ? label : string.Concat(label.AsSpan(0, 45), "...");
    }

    private static void ShowMainWindow()
    {
        if (s_mainWindow is null)
        {
            return;
        }

        s_mainWindow.Show();
        s_mainWindow.Activate();
    }
}
