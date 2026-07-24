namespace Snippets.Core.Config;

public sealed record SnippetsConfig(
    string Schema,
    WorkspaceConfig Workspace,
    AppConfig App,
    ClipsConfig Clips,
    NotesConfig Notes,
    JobsConfig Jobs)
{
    public static string DefaultConfigPath(string? userProfile = null, string? localAppData = null)
    {
        var home = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "snippets-config.yml");
    }

    public static SnippetsConfig Load(string? path = null, string? userProfile = null, string? localAppData = null)
    {
        var config = CreateDefault(userProfile, localAppData);
        var configPath = path ?? DefaultConfigPath(userProfile, localAppData);
        if (!File.Exists(configPath))
        {
            WriteDefaultConfig(config, configPath);
            return config;
        }

        return SnippetsYamlConfigParser.ReadConfig(File.ReadAllLines(configPath), config, userProfile, localAppData);
    }

    public static SnippetsConfig SaveAppSettings(
        bool tray,
        bool startWithSystem,
        string? path = null,
        string? userProfile = null,
        string? localAppData = null)
    {
        var configPath = path ?? DefaultConfigPath(userProfile, localAppData);
        if (!File.Exists(configPath))
        {
            WriteDefaultConfig(CreateDefault(userProfile, localAppData), configPath);
        }

        var lines = SnippetsYamlConfigParser.UpdateAppSettings(File.ReadAllLines(configPath), tray, startWithSystem);
        File.WriteAllLines(configPath, lines);
        return Load(configPath, userProfile, localAppData);
    }

    public static SnippetsConfig CreateDefault(string? userProfile = null, string? localAppData = null)
    {
        var home = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(home, "Documents", "Snippets");

        return new SnippetsConfig(
            "snippets-v1",
            new WorkspaceConfig(root),
            new AppConfig(true, true, AppConfig.DefaultTrayQuickLimit, Path.Combine(local, "Snippets", "logs")),
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
                        "clipboard cleaner",
                        JobTriggerConfig.Startup(),
                        "clip.prune")
                ]));
    }

    private static void WriteDefaultConfig(SnippetsConfig config, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, FormatDefaultConfig(config));
    }

    private static string FormatDefaultConfig(SnippetsConfig config)
    {
        return
            $$"""
            schema: {{config.Schema}}

            workspace:
              root: '${USERPROFILE}/Documents/Snippets'

            app:
              tray: {{FormatBool(config.App.Tray)}}
              startWithSystem: {{FormatBool(config.App.StartWithSystem)}}
              trayQuickLimit: {{config.App.TrayQuickLimit}}
              logs: '${LOCALAPPDATA}/Snippets/logs'

            clips:
              autoSave: '${workspace.root}/Clips/AutoSave'
              favorites: '${workspace.root}/Clips/Favorites'
              maxAutoSave: {{config.Clips.MaxAutoSave}}
              dedupeCacheWindow: {{FormatDuration(config.Clips.DedupeCacheWindow)}}

            notes:
              drafts: '${workspace.root}/Notes/Drafts'

            jobs:
              enabled: {{FormatBool(config.Jobs.Enabled)}}
              items:
                - id: clip-poll
                  name: clipboard watcher
                  enabled: true
                  trigger:
                    type: interval
                    every: 1s
                  action:
                    type: tool
                    name: clip.poll

                - id: clip-prune
                  name: clipboard cleaner
                  enabled: true
                  trigger:
                    type: startup
                  action:
                    type: tool
                    name: clip.prune
            """;
    }

    private static string FormatBool(bool value) => value ? "true" : "false";

    private static string FormatDuration(TimeSpan? value)
    {
        return value is null ? "null" : $"{value.Value.TotalMinutes:0.#}m";
    }
}

internal static class SnippetsYamlConfigParser
{
    public static IReadOnlyList<string> UpdateAppSettings(IReadOnlyList<string> lines, bool tray, bool startWithSystem)
    {
        var output = lines.ToList();
        var appIndex = output.FindIndex(line => line.Trim() == "app:");
        if (appIndex < 0)
        {
            output.Add(string.Empty);
            output.Add("app:");
            output.Add($"  tray: {FormatBool(tray)}");
            output.Add($"  startWithSystem: {FormatBool(startWithSystem)}");
            output.Add($"  trayQuickLimit: {AppConfig.DefaultTrayQuickLimit}");
            return output;
        }

        var appEnd = appIndex + 1;
        while (appEnd < output.Count && (string.IsNullOrWhiteSpace(output[appEnd]) || char.IsWhiteSpace(output[appEnd][0])))
        {
            appEnd++;
        }

        var trayIndex = FindSectionKey(output, appIndex + 1, appEnd, "tray");
        var startupIndex = FindSectionKey(output, appIndex + 1, appEnd, "startWithSystem");
        if (trayIndex >= 0)
        {
            output[trayIndex] = $"{LeadingWhitespace(output[trayIndex])}tray: {FormatBool(tray)}";
        }
        else
        {
            output.Insert(appIndex + 1, $"  tray: {FormatBool(tray)}");
            appEnd++;
            if (startupIndex >= appIndex + 1)
            {
                startupIndex++;
            }
        }

        if (startupIndex >= 0)
        {
            output[startupIndex] = $"{LeadingWhitespace(output[startupIndex])}startWithSystem: {FormatBool(startWithSystem)}";
        }
        else
        {
            output.Insert(appIndex + 2, $"  startWithSystem: {FormatBool(startWithSystem)}");
        }

        appEnd = appIndex + 1;
        while (appEnd < output.Count && (string.IsNullOrWhiteSpace(output[appEnd]) || char.IsWhiteSpace(output[appEnd][0])))
        {
            appEnd++;
        }

        var quickLimitIndex = FindSectionKey(output, appIndex + 1, appEnd, "trayQuickLimit");
        if (quickLimitIndex < 0)
        {
            var insertAfter = FindSectionKey(output, appIndex + 1, appEnd, "startWithSystem");
            output.Insert((insertAfter >= 0 ? insertAfter : appIndex) + 1, $"  trayQuickLimit: {AppConfig.DefaultTrayQuickLimit}");
        }

        return output;
    }

    public static SnippetsConfig ReadConfig(
        IReadOnlyList<string> lines,
        SnippetsConfig defaults,
        string? userProfile,
        string? localAppData)
    {
        var values = ReadScalarSections(lines);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["USERPROFILE"] = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["LOCALAPPDATA"] = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        var workspaceRoot = Expand(
            values.GetValueOrDefault("workspace.root") ?? defaults.Workspace.Root,
            variables);
        variables["workspace.root"] = workspaceRoot;

        var app = new AppConfig(
            ReadOptionalBool(values.GetValueOrDefault("app.tray"), defaults.App.Tray),
            ReadOptionalBool(values.GetValueOrDefault("app.startWithSystem"), defaults.App.StartWithSystem),
            ReadPositiveInt(values.GetValueOrDefault("app.trayQuickLimit"), defaults.App.TrayQuickLimit),
            Expand(values.GetValueOrDefault("app.logs") ?? defaults.App.Logs, variables));
        var clips = new ClipsConfig(
            Expand(values.GetValueOrDefault("clips.autoSave") ?? defaults.Clips.AutoSave, variables),
            Expand(values.GetValueOrDefault("clips.favorites") ?? defaults.Clips.Favorites, variables),
            ReadInt(values.GetValueOrDefault("clips.maxAutoSave"), defaults.Clips.MaxAutoSave),
            ReadOptionalDuration(values.GetValueOrDefault("clips.dedupeCacheWindow"), defaults.Clips.DedupeCacheWindow));
        var notes = new NotesConfig(
            Expand(values.GetValueOrDefault("notes.drafts") ?? defaults.Notes.Drafts, variables));
        var jobs = TryReadJobs(lines) ?? defaults.Jobs;

        return defaults with
        {
            Workspace = new WorkspaceConfig(workspaceRoot),
            App = app,
            Clips = clips,
            Notes = notes,
            Jobs = jobs
        };
    }

    public static JobsConfig? TryReadJobs(IEnumerable<string> lines)
    {
        var jobsLines = ReadJobsBlock(lines).ToList();
        if (jobsLines.Count == 0)
        {
            return null;
        }

        var enabled = true;
        var jobs = new List<JobConfig>();
        JobBuilder? current = null;
        var section = string.Empty;
        var subsection = string.Empty;

        foreach (var raw in jobsLines)
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("- id:", StringComparison.Ordinal))
            {
                AddCurrent();
                current = new JobBuilder { Id = ReadValue(line["- id:".Length..]) };
                section = string.Empty;
                subsection = string.Empty;
                continue;
            }

            if (current is null)
            {
                if (line.StartsWith("enabled:", StringComparison.Ordinal))
                {
                    enabled = ReadBool(line["enabled:".Length..], defaultValue: true);
                }

                continue;
            }

            switch (line)
            {
                case "trigger:":
                    section = "trigger";
                    subsection = string.Empty;
                    continue;
                case "action:":
                    section = "action";
                    subsection = string.Empty;
                    continue;
                case "args:":
                    subsection = "args";
                    continue;
                case "env:":
                    subsection = "env";
                    continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) && section == "action" && subsection == "args")
            {
                current.CommandArgs.Add(ReadValue(line[2..]));
                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = ReadValue(line[(separator + 1)..]);

            if (section == "trigger")
            {
                ReadTrigger(current, key, value);
            }
            else if (section == "action")
            {
                ReadAction(current, subsection, key, value);
            }
            else
            {
                ReadJob(current, key, value);
            }
        }

        AddCurrent();
        return jobs.Count == 0 ? null : new JobsConfig(enabled, jobs);

        void AddCurrent()
        {
            if (current is not null)
            {
                jobs.Add(current.Build());
            }
        }
    }

    private static IEnumerable<string> ReadJobsBlock(IEnumerable<string> lines)
    {
        var inJobs = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (!inJobs)
            {
                if (line.Trim() == "jobs:")
                {
                    inJobs = true;
                }

                continue;
            }

            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                yield break;
            }

            yield return line;
        }
    }

    private static Dictionary<string, string> ReadScalarSections(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var section = string.Empty;
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmedEnd = raw.TrimEnd();
            if (!char.IsWhiteSpace(trimmedEnd[0]))
            {
                var top = StripComment(trimmedEnd).Trim();
                if (top.EndsWith(':'))
                {
                    section = top[..^1];
                }

                continue;
            }

            if (section is "jobs" or "")
            {
                continue;
            }

            var line = StripComment(trimmedEnd).Trim();
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            values[$"{section}.{line[..separator].Trim()}"] = ReadValue(line[(separator + 1)..]);
        }

        return values;
    }

    private static void ReadJob(JobBuilder job, string key, string value)
    {
        switch (key)
        {
            case "id":
                job.Id = value;
                break;
            case "name":
                job.Name = value;
                break;
            case "enabled":
                job.Enabled = ReadBool(value, defaultValue: true);
                break;
        }
    }

    private static void ReadTrigger(JobBuilder job, string key, string value)
    {
        switch (key)
        {
            case "type":
                job.TriggerType = value;
                break;
            case "every":
                job.Every = ReadDuration(value);
                break;
            case "expression":
                job.Expression = value;
                break;
        }
    }

    private static void ReadAction(JobBuilder job, string subsection, string key, string value)
    {
        if (key is "timeout" or "timeoutSeconds")
        {
            job.Timeout = ReadDuration(value);
            return;
        }

        if (subsection == "args")
        {
            job.ToolArgs[key] = value;
            return;
        }

        if (subsection == "env")
        {
            job.Env[key] = value;
            return;
        }

        switch (key)
        {
            case "type":
                job.ActionType = value;
                break;
            case "name":
                job.ToolName = value;
                break;
            case "command":
                job.Command = value;
                break;
        }
    }

    private static string StripComment(string value)
    {
        var index = value.IndexOf('#', StringComparison.Ordinal);
        return index < 0 ? value : value[..index];
    }

    private static string ReadValue(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\''))
            ? trimmed[1..^1]
            : trimmed;
    }

    private static bool ReadBool(string value, bool defaultValue)
    {
        return bool.TryParse(ReadValue(value), out var parsed) ? parsed : defaultValue;
    }

    private static int FindSectionKey(IReadOnlyList<string> lines, int start, int end, string key)
    {
        for (var index = start; index < end; index++)
        {
            var line = StripComment(lines[index]).Trim();
            if (line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string LeadingWhitespace(string value)
    {
        var count = 0;
        while (count < value.Length && char.IsWhiteSpace(value[count]))
        {
            count++;
        }

        return value[..count];
    }

    private static string FormatBool(bool value) => value ? "true" : "false";

    private static bool ReadOptionalBool(string? value, bool defaultValue)
    {
        return value is null ? defaultValue : ReadBool(value, defaultValue);
    }

    private static int ReadInt(string? value, int defaultValue)
    {
        return value is not null && int.TryParse(ReadValue(value), out var parsed) ? parsed : defaultValue;
    }

    private static int ReadPositiveInt(string? value, int defaultValue)
    {
        return value is not null && int.TryParse(ReadValue(value), out var parsed) && parsed > 0
            ? parsed
            : defaultValue;
    }

    private static TimeSpan? ReadOptionalDuration(string? value, TimeSpan? defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        return string.Equals(ReadValue(value), "null", StringComparison.OrdinalIgnoreCase)
            ? null
            : ReadDuration(value);
    }

    private static string Expand(string value, IReadOnlyDictionary<string, string> variables)
    {
        var expanded = ReadValue(value);
        foreach (var (key, replacement) in variables)
        {
            expanded = expanded.Replace("${" + key + "}", replacement, StringComparison.OrdinalIgnoreCase);
        }

        return Environment.ExpandEnvironmentVariables(expanded)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static TimeSpan ReadDuration(string value)
    {
        var raw = ReadValue(value);
        if (TimeSpan.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        if (raw.EndsWith("ms", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(raw[..^2], out var milliseconds))
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        if (raw.EndsWith('s') && double.TryParse(raw[..^1], out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (raw.EndsWith('m') && double.TryParse(raw[..^1], out var minutes))
        {
            return TimeSpan.FromMinutes(minutes);
        }

        if (raw.EndsWith('h') && double.TryParse(raw[..^1], out var hours))
        {
            return TimeSpan.FromHours(hours);
        }

        return double.TryParse(raw, out var plainSeconds)
            ? TimeSpan.FromSeconds(plainSeconds)
            : throw new FormatException($"Duration '{value}' is not supported.");
    }

    private sealed class JobBuilder
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TriggerType { get; set; } = "manual";
        public TimeSpan? Every { get; set; }
        public string? Expression { get; set; }
        public string ActionType { get; set; } = "tool";
        public string? ToolName { get; set; }
        public string? Command { get; set; }
        public List<string> CommandArgs { get; } = [];
        public Dictionary<string, string> ToolArgs { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Env { get; } = new(StringComparer.Ordinal);
        public TimeSpan? Timeout { get; set; }
        public bool Enabled { get; set; } = true;

        public JobConfig Build()
        {
            var trigger = TriggerType switch
            {
                "startup" => JobTriggerConfig.Startup(),
                "interval" => JobTriggerConfig.Interval(Every ?? throw new InvalidOperationException($"Job '{Id}' interval trigger requires 'every'.")),
                "cron" => JobTriggerConfig.Cron(Expression ?? throw new InvalidOperationException($"Job '{Id}' cron trigger requires 'expression'.")),
                _ => JobTriggerConfig.Manual()
            };

            return ActionType == "command"
                ? JobConfig.CreateCommand(Id, Name, trigger, Command ?? throw new InvalidOperationException($"Job '{Id}' command action requires 'command'."), CommandArgs, Env, Timeout, Enabled)
                : JobConfig.CreateTool(Id, Name, trigger, ToolName ?? throw new InvalidOperationException($"Job '{Id}' tool action requires 'name'."), ToolArgs, Enabled);
        }
    }
}

public sealed record WorkspaceConfig(string Root);

public sealed record AppConfig(bool Tray, bool StartWithSystem, int TrayQuickLimit, string Logs)
{
    public const int DefaultTrayQuickLimit = 10;
}

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
        TimeSpan? timeout = null,
        bool enabled = true)
    {
        return new JobConfig(id, name, trigger, JobActionConfig.ExternalCommand(command, args, env, timeout), enabled);
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
    IReadOnlyDictionary<string, string>? Env = null,
    TimeSpan? Timeout = null)
{
    public static JobActionConfig Tool(string name, IReadOnlyDictionary<string, string>? args = null)
    {
        return new JobActionConfig("tool", Name: name, Args: args ?? new Dictionary<string, string>());
    }

    public static JobActionConfig ExternalCommand(
        string command,
        IReadOnlyList<string>? args = null,
        IReadOnlyDictionary<string, string>? env = null,
        TimeSpan? timeout = null)
    {
        return new JobActionConfig(
            "command",
            Command: command,
            CommandArgs: args ?? [],
            Env: env ?? new Dictionary<string, string>(),
            Timeout: timeout);
    }
}
