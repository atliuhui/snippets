using System.Diagnostics;
using Snippets.Core.Config;

namespace Snippets.Core.Jobs;

public sealed record JobExecutionResult(
    string JobId,
    bool Succeeded,
    int? ExitCode = null,
    string? Output = null,
    string? Error = null);

public delegate Task<string?> SnippetsToolHandler(IReadOnlyDictionary<string, string> args, CancellationToken cancellationToken);

public sealed class ToolRegistry
{
    private readonly Dictionary<string, SnippetsToolHandler> _handlers = new(StringComparer.Ordinal);

    public void Register(string name, SnippetsToolHandler handler)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tool name is required.", nameof(name));
        }

        _handlers[name] = handler;
    }

    public Task<string?> ExecuteAsync(string name, IReadOnlyDictionary<string, string> args, CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(name, out var handler))
        {
            throw new InvalidOperationException($"Tool '{name}' is not registered.");
        }

        return handler(args, cancellationToken);
    }
}

public sealed class JobRunner
{
    private readonly ToolRegistry _tools;

    public JobRunner(ToolRegistry tools)
    {
        _tools = tools;
    }

    public async Task<JobExecutionResult> RunOnceAsync(JobConfig job, CancellationToken cancellationToken = default)
    {
        if (!job.Enabled)
        {
            return new JobExecutionResult(job.Id, Succeeded: false, Error: "Job is disabled.");
        }

        return job.Action.Type switch
        {
            "tool" => await RunToolAsync(job, cancellationToken),
            "command" => await RunCommandAsync(job, cancellationToken),
            _ => throw new NotSupportedException($"Job action type '{job.Action.Type}' is not supported.")
        };
    }

    private async Task<JobExecutionResult> RunToolAsync(JobConfig job, CancellationToken cancellationToken)
    {
        var name = job.Action.Name ?? throw new InvalidOperationException("Tool action requires a name.");
        var output = await _tools.ExecuteAsync(name, job.Action.Args ?? new Dictionary<string, string>(), cancellationToken);
        return new JobExecutionResult(job.Id, Succeeded: true, Output: output);
    }

    private static async Task<JobExecutionResult> RunCommandAsync(JobConfig job, CancellationToken cancellationToken)
    {
        var command = job.Action.Command ?? throw new InvalidOperationException("Command action requires a command.");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in job.Action.CommandArgs ?? [])
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in job.Action.Env ?? new Dictionary<string, string>())
        {
            process.StartInfo.Environment[key] = value;
        }

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new JobExecutionResult(
            job.Id,
            process.ExitCode == 0,
            process.ExitCode,
            await outputTask,
            await errorTask);
    }
}
