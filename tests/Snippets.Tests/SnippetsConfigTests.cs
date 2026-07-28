using Snippets.Core.Config;

namespace Snippets.Tests;

public sealed class SnippetsConfigTests
{
    [Fact]
    public void CreateDefault_matches_readme_storage_conventions()
    {
        var config = SnippetsConfig.CreateDefault(Path.Combine(@"C:\", "Users", "Alice"), Path.Combine(@"C:\", "Users", "Alice", "AppData", "Local"));

        Assert.Equal("snippets-v1", config.Schema);
        Assert.EndsWith(Path.Combine("Documents", "Snippets"), config.Workspace.Root);
        Assert.EndsWith(Path.Combine("Clips", "AutoSave"), config.Clips.AutoSave);
        Assert.EndsWith(Path.Combine("Clips", "Favorites"), config.Clips.Favorites);
        Assert.EndsWith(Path.Combine("Notes", "Drafts"), config.Notes.Drafts);
        Assert.False(config.App.CloseToTray);
        Assert.False(config.App.StartWithSystem);
        Assert.Equal(10, config.App.TrayQuickLimit);
        Assert.Contains(config.Jobs.Items, job => job.Id == "clip-poll" && job.Action.Name == "clip.poll");
        Assert.Contains(config.Jobs.Items, job => job.Id == "clip-prune" && job.Trigger.Type == "startup");
        Assert.EndsWith(Path.Combine("Users", "Alice", "snippets-config.yml"), SnippetsConfig.DefaultConfigPath(Path.Combine(@"C:\", "Users", "Alice"), Path.Combine(@"C:\", "Users", "Alice", "AppData", "Local")));
    }

    [Fact]
    public void Load_reads_jobs_from_snippets_config_yml()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnippetsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "snippets-config.yml");
        File.WriteAllText(
            configPath,
            """
            schema: snippets-v1
            workspace:
              root: "${USERPROFILE}/CustomSnippets"
            app:
              trayQuickLimit: 6
            clips:
              autoSave: "${workspace.root}/Auto"
              favorites: "${workspace.root}/Fav"
            notes:
              drafts: "${workspace.root}/Drafts"
            jobs:
              enabled: true
              items:
                - id: backup-daily
                  name: daily backup
                  trigger:
                    type: manual
                  action:
                    type: command
                    command: "${USERPROFILE}/tools/dotnet"
                    args:
                      - --version
                      - "${workspace.root}/scripts/backup.ps1"
                    env:
                      NODE_ENV: production
                    timeout: 180s
                  enabled: true
            """);

        var config = SnippetsConfig.Load(configPath, root, Path.Combine(root, "LocalAppData"));

        Assert.Equal(Path.Combine(root, "CustomSnippets"), config.Workspace.Root);
        Assert.Equal(Path.Combine(root, "CustomSnippets", "Auto"), config.Clips.AutoSave);
        Assert.Equal(Path.Combine(root, "CustomSnippets", "Fav"), config.Clips.Favorites);
        Assert.Equal(Path.Combine(root, "CustomSnippets", "Drafts"), config.Notes.Drafts);
        Assert.Equal(6, config.App.TrayQuickLimit);
        var job = Assert.Single(config.Jobs.Items);
        Assert.Equal("backup-daily", job.Id);
        Assert.Equal("manual", job.Trigger.Type);
        Assert.Equal("command", job.Action.Type);
        Assert.Equal(Path.Combine(root, "tools", "dotnet"), job.Action.Command);
        Assert.Equal(
            ["--version", Path.Combine(root, "CustomSnippets", "scripts", "backup.ps1")],
            job.Action.CommandArgs);
        Assert.Equal("production", job.Action.Env?["NODE_ENV"]);
        Assert.Equal(TimeSpan.FromSeconds(180), job.Action.Timeout);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Load_creates_default_config_in_user_profile()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnippetsTests", Guid.NewGuid().ToString("N"));
        var userProfile = Path.Combine(root, "User");
        var localAppData = Path.Combine(root, "LocalAppData");

        var config = SnippetsConfig.Load(userProfile: userProfile, localAppData: localAppData);
        var configPath = Path.Combine(userProfile, "snippets-config.yml");

        Assert.True(File.Exists(configPath));
        var text = File.ReadAllText(configPath);
        Assert.Contains("schema: snippets-v1", text);
        Assert.Contains("${USERPROFILE}/Documents/Snippets", text);
        Assert.Contains("closeToTray: false", text);
        Assert.Contains("startWithSystem: false", text);
        Assert.Contains("trayQuickLimit: 10", text);
        Assert.Contains("name: clipboard cleaner", text);
        Assert.Contains(
            """
                - id: clip-poll
                  name: clipboard watcher
                  enabled: true
                  trigger:
                    type: interval
            """,
            text);
        Assert.EndsWith(Path.Combine("User", "snippets-config.yml"), configPath);
        Assert.EndsWith(Path.Combine("Documents", "Snippets"), config.Workspace.Root);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Load_uses_default_tray_quick_limit_for_invalid_values()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnippetsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "snippets-config.yml");
        File.WriteAllText(
            configPath,
            """
            schema: snippets-v1

            app:
              trayQuickLimit: 0
            """);

        var config = SnippetsConfig.Load(configPath, root, Path.Combine(root, "LocalAppData"));

        Assert.Equal(10, config.App.TrayQuickLimit);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void SaveAppSettings_updates_close_to_tray_and_startup_values()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnippetsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "snippets-config.yml");
        File.WriteAllText(
            configPath,
            """
            schema: snippets-v1

            app:
              closeToTray: true
              startWithSystem: true
              logs: "${LOCALAPPDATA}/Snippets/logs"

            jobs:
              enabled: true
              items:
                - id: manual-job
                  name: manual job
                  trigger:
                    type: manual
                  action:
                    type: command
                    command: dotnet
            """);

        var config = SnippetsConfig.SaveAppSettings(
            closeToTray: false,
            startWithSystem: false,
            path: configPath,
            userProfile: root,
            localAppData: Path.Combine(root, "LocalAppData"));

        Assert.False(config.App.CloseToTray);
        Assert.False(config.App.StartWithSystem);
        Assert.Equal(10, config.App.TrayQuickLimit);
        var text = File.ReadAllText(configPath);
        Assert.Contains("closeToTray: false", text);
        Assert.Contains("startWithSystem: false", text);
        Assert.Contains("trayQuickLimit: 10", text);
        Assert.Contains("logs: \"${LOCALAPPDATA}/Snippets/logs\"", text);

        Directory.Delete(root, recursive: true);
    }
}
