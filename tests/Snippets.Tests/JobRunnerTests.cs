using Snippets.Core.Config;
using Snippets.Core.Jobs;

namespace Snippets.Tests;

public sealed class JobRunnerTests
{
    [Fact]
    public async Task RunOnceAsync_executes_registered_tool_action()
    {
        var registry = new ToolRegistry();
        registry.Register("clip.prune", (args, _) => Task.FromResult<string?>($"max={args["maxAutoSave"]}"));
        var runner = new JobRunner(registry);
        var job = JobConfig.CreateTool(
            "clip-prune",
            "prune clips",
            JobTriggerConfig.Startup(),
            "clip.prune",
            new Dictionary<string, string> { ["maxAutoSave"] = "100" });

        var result = await runner.RunOnceAsync(job);

        Assert.True(result.Succeeded);
        Assert.Equal("max=100", result.Output);
    }

    [Fact]
    public async Task RunOnceAsync_executes_command_action()
    {
        var runner = new JobRunner(new ToolRegistry());
        var job = JobConfig.CreateCommand(
            "dotnet-version",
            "dotnet version",
            JobTriggerConfig.Manual(),
            "dotnet",
            ["--version"]);

        var result = await runner.RunOnceAsync(job);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
    }

    [Fact]
    public async Task RunOnceAsync_truncates_large_command_output()
    {
        var runner = new JobRunner(new ToolRegistry());
        var job = JobConfig.CreateCommand(
            "large-output",
            "large output",
            JobTriggerConfig.Manual(),
            "powershell",
            ["-NoProfile", "-Command", "$text = 'x' * 40000; Write-Output $text"]);

        var result = await runner.RunOnceAsync(job);

        Assert.True(result.Succeeded, result.Error);
        Assert.NotNull(result.Output);
        Assert.True(result.Output.Length < 40000);
        Assert.Contains("[output truncated]", result.Output);
    }

    [Fact]
    public void CreateCommandStartInfo_runs_without_window()
    {
        var job = JobConfig.CreateCommand(
            "echo",
            "echo",
            JobTriggerConfig.Manual(),
            "powershell",
            ["-NoProfile", "-Command", "Write-Output ok"]);

        var startInfo = JobRunner.CreateCommandStartInfo(job, job.Action.Command!);

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public async Task RunOnceAsync_times_out_command_action()
    {
        var runner = new JobRunner(new ToolRegistry());
        var job = JobConfig.CreateCommand(
            "slow-command",
            "slow command",
            JobTriggerConfig.Manual(),
            "powershell",
            ["-NoProfile", "-Command", "Start-Sleep -Seconds 5"],
            timeout: TimeSpan.FromMilliseconds(100));

        var result = await runner.RunOnceAsync(job);

        Assert.False(result.Succeeded);
        Assert.Contains("timed out", result.Error);
    }

    [Fact]
    public async Task StartAsync_runs_startup_jobs_and_schedules_interval_jobs()
    {
        var startupRuns = 0;
        var intervalRuns = 0;
        var intervalCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ToolRegistry();
        registry.Register("clip.prune", (_, _) =>
        {
            startupRuns++;
            return Task.FromResult<string?>("deleted=0");
        });
        registry.Register("clip.poll", (_, _) =>
        {
            if (Interlocked.Increment(ref intervalRuns) >= 2)
            {
                intervalCompleted.TrySetResult();
            }

            return Task.FromResult<string?>(null);
        });
        using var scheduler = new JobScheduler(
            new JobRunner(registry),
            (_, cancellationToken) => Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken));
        var config = new JobsConfig(
            true,
            [
                JobConfig.CreateTool("clip-prune", "prune clips", JobTriggerConfig.Startup(), "clip.prune"),
                JobConfig.CreateTool("clip-poll", "clipboard watcher", JobTriggerConfig.Interval(TimeSpan.FromMilliseconds(1)), "clip.poll")
            ]);

        var startupResults = await scheduler.StartAsync(config);
        await intervalCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Single(startupResults);
        Assert.True(startupResults[0].Succeeded);
        Assert.Equal(1, startupRuns);
        Assert.True(intervalRuns >= 2);
    }

    [Fact]
    public async Task StartAsync_schedules_cron_jobs()
    {
        var cronRuns = 0;
        var cronCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ToolRegistry();
        registry.Register("cron.tool", (_, _) =>
        {
            if (Interlocked.Increment(ref cronRuns) >= 2)
            {
                cronCompleted.TrySetResult();
            }

            return Task.FromResult<string?>("done");
        });
        using var scheduler = new JobScheduler(
            new JobRunner(registry),
            (_, cancellationToken) => Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken));
        var job = JobConfig.CreateTool(
            "cron-job",
            "cron job",
            JobTriggerConfig.Cron("*/2 * * * * *"),
            "cron.tool");

        await scheduler.StartAsync(new JobsConfig(true, [job]));
        await cronCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(scheduler.IsJobRunning("cron-job"));
        Assert.True(cronRuns >= 2);
    }

    [Fact]
    public async Task StopJob_stops_cron_job()
    {
        var registry = new ToolRegistry();
        registry.Register("cron.tool", (_, _) => Task.FromResult<string?>("done"));
        using var scheduler = new JobScheduler(
            new JobRunner(registry),
            (_, cancellationToken) => Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken));
        var job = JobConfig.CreateTool(
            "cron-job",
            "cron job",
            JobTriggerConfig.Cron("*/2 * * * * *"),
            "cron.tool");
        await scheduler.StartAsync(new JobsConfig(true, [job]));

        var stopped = scheduler.StopJob("cron-job");

        Assert.True(stopped);
        Assert.False(scheduler.IsJobRunning("cron-job"));
    }

    [Fact]
    public async Task RunManualAsync_executes_configured_job_by_id()
    {
        var registry = new ToolRegistry();
        registry.Register("clip.prune", (_, _) => Task.FromResult<string?>("deleted=3"));
        using var scheduler = new JobScheduler(new JobRunner(registry));
        var config = new JobsConfig(
            true,
            [JobConfig.CreateTool("clip-prune", "prune clips", JobTriggerConfig.Manual(), "clip.prune")]);
        await scheduler.StartAsync(config);

        var result = await scheduler.RunManualAsync("clip-prune");

        Assert.True(result.Succeeded);
        Assert.Equal("deleted=3", result.Output);
    }

    [Fact]
    public async Task RunManualAsync_rejects_duplicate_execution_for_same_job()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ToolRegistry();
        registry.Register("slow.tool", async (_, _) =>
        {
            started.SetResult();
            await release.Task;
            return "done";
        });
        using var scheduler = new JobScheduler(new JobRunner(registry));
        var job = JobConfig.CreateTool("slow-job", "slow job", JobTriggerConfig.Manual(), "slow.tool");
        await scheduler.StartAsync(new JobsConfig(true, [job]));

        var first = scheduler.RunManualAsync("slow-job");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var second = await scheduler.RunManualAsync("slow-job");
        release.SetResult();
        var firstResult = await first;

        Assert.False(second.Succeeded);
        Assert.Equal("Job is already running.", second.Error);
        Assert.True(firstResult.Succeeded);
    }
}
