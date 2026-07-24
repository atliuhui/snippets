namespace Snippets.Core.Config;

public sealed record SnippetsConfig(
    string Schema,
    WorkspaceConfig Workspace,
    AppConfig App,
    ClipsConfig Clips,
    NotesConfig Notes,
    JobsConfig Jobs)
{
    public static SnippetsConfig CreateDefault(string? userProfile = null, string? localAppData = null)
    {
        var home = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(home, "Documents", "Snippets");

        return new SnippetsConfig(
            "snippets-v1",
            new WorkspaceConfig(root),
            new AppConfig(true, true, Path.Combine(local, "Snippets", "logs")),
            new ClipsConfig(
                Path.Combine(root, "Clips", "AutoSave"),
                Path.Combine(root, "Clips", "Favorites"),
                100,
                TimeSpan.FromMinutes(10)),
            new NotesConfig(Path.Combine(root, "Notes", "Drafts")),
            new JobsConfig(
                true,
                [
                    JobConfig.CreateTool(
                        "clip-poll",
                        "clipboard watcher",
                        JobTriggerConfig.Interval(TimeSpan.FromSeconds(1)),
                        "clip.poll"),
                    JobConfig.CreateTool(
                        "clip-prune",
                        "prune clips",
                        JobTriggerConfig.Startup(),
                        "clip.prune",
                        new Dictionary<string, string> { ["maxAutoSave"] = "100" })
                ]));
    }
}

public sealed record WorkspaceConfig(string Root);

public sealed record AppConfig(bool Tray, bool StartWithSystem, string Logs);

public sealed record ClipsConfig(
    string AutoSave,
    string Favorites,
    int MaxAutoSave,
    TimeSpan? DedupeCacheWindow);

public sealed record NotesConfig(string Drafts);

public sealed record JobsConfig(bool Enabled, IReadOnlyList<JobConfig> Items);

public sealed record JobConfig(
    string Id,
    string Name,
    JobTriggerConfig Trigger,
    JobActionConfig Action,
    bool Enabled)
{
    public static JobConfig CreateTool(
        string id,
        string name,
        JobTriggerConfig trigger,
        string toolName,
        IReadOnlyDictionary<string, string>? args = null,
        bool enabled = true)
    {
        return new JobConfig(id, name, trigger, JobActionConfig.Tool(toolName, args), enabled);
    }

    public static JobConfig CreateCommand(
        string id,
        string name,
        JobTriggerConfig trigger,
        string command,
        IReadOnlyList<string>? args = null,
        IReadOnlyDictionary<string, string>? env = null,
        bool enabled = true)
    {
        return new JobConfig(id, name, trigger, JobActionConfig.ExternalCommand(command, args, env), enabled);
    }
}

public sealed record JobTriggerConfig(string Type, TimeSpan? Every = null, string? Expression = null)
{
    public static JobTriggerConfig Startup() => new("startup");

    public static JobTriggerConfig Manual() => new("manual");

    public static JobTriggerConfig Interval(TimeSpan every) => new("interval", every);

    public static JobTriggerConfig Cron(string expression) => new("cron", Expression: expression);
}

public sealed record JobActionConfig(
    string Type,
    string? Name = null,
    IReadOnlyDictionary<string, string>? Args = null,
    string? Command = null,
    IReadOnlyList<string>? CommandArgs = null,
    IReadOnlyDictionary<string, string>? Env = null)
{
    public static JobActionConfig Tool(string name, IReadOnlyDictionary<string, string>? args = null)
    {
        return new JobActionConfig("tool", Name: name, Args: args ?? new Dictionary<string, string>());
    }

    public static JobActionConfig ExternalCommand(
        string command,
        IReadOnlyList<string>? args = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        return new JobActionConfig(
            "command",
            Command: command,
            CommandArgs: args ?? [],
            Env: env ?? new Dictionary<string, string>());
    }
}
