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
    internal const int MaxCapturedOutputChars = 32 * 1024;

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
        var timeout = job.Action.Timeout.GetValueOrDefault();
        var hasTimeout = job.Action.Timeout.HasValue;
        using var timeoutSource = hasTimeout
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        if (timeoutSource is not null)
        {
            timeoutSource.CancelAfter(timeout);
        }

        var runToken = timeoutSource?.Token ?? cancellationToken;
        using var process = new Process();
        process.StartInfo = CreateCommandStartInfo(job, command);

        process.Start();
        var outputTask = ReadLimitedOutputAsync(process.StandardOutput, runToken);
        var errorTask = ReadLimitedOutputAsync(process.StandardError, runToken);
        try
        {
            await process.WaitForExitAsync(runToken);
        }
        catch (OperationCanceledException) when (timeoutSource?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }

            return new JobExecutionResult(
                job.Id,
                Succeeded: false,
                ExitCode: process.HasExited ? process.ExitCode : null,
                Output: await ReadCompletedOutputAsync(outputTask),
                Error: $"Command timed out after {FormatTimeout(timeout)}.");
        }

        return new JobExecutionResult(
            job.Id,
            process.ExitCode == 0,
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    internal static ProcessStartInfo CreateCommandStartInfo(JobConfig job, string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in job.Action.CommandArgs ?? [])
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in job.Action.Env ?? new Dictionary<string, string>())
        {
            startInfo.Environment[key] = value;
        }

        return startInfo;
    }

    private static async Task<string> ReadCompletedOutputAsync(Task<string> outputTask)
    {
        return outputTask.IsCompletedSuccessfully
            ? await outputTask
            : string.Empty;
    }

    private static async Task<string> ReadLimitedOutputAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var builder = new System.Text.StringBuilder(capacity: Math.Min(MaxCapturedOutputChars, buffer.Length));
        var truncated = false;

        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            var remaining = MaxCapturedOutputChars - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }

            if (read > remaining)
            {
                truncated = true;
            }
        }

        if (truncated)
        {
            builder.AppendLine();
            builder.Append("[output truncated]");
        }

        return builder.ToString();
    }

    private static string FormatTimeout(TimeSpan timeout)
    {
        return timeout.TotalSeconds < 60
            ? $"{timeout.TotalSeconds:0.#} seconds"
            : $"{timeout.TotalMinutes:0.#} minutes";
    }
}
