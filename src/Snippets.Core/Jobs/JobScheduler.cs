using Snippets.Core.Config;

namespace Snippets.Core.Jobs;

public sealed record JobRunRecord(
    JobConfig Job,
    JobExecutionResult Result,
    DateTimeOffset FinishedAt);

public sealed class JobScheduler : IDisposable
{
    private readonly JobRunner _runner;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Dictionary<string, JobConfig> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _scheduledTokens = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeRuns = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private bool _disposed;

    public JobScheduler(
        JobRunner runner,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _runner = runner;
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public event Action<JobRunRecord>? JobCompleted;
    public event Action<JobConfig>? JobStarted;

    public async Task<IReadOnlyList<JobExecutionResult>> StartAsync(
        JobsConfig config,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopAll();

        lock (_sync)
        {
            _jobs.Clear();
            foreach (var job in config.Items)
            {
                _jobs[job.Id] = job;
            }
        }

        if (!config.Enabled)
        {
            return [];
        }

        var startupResults = new List<JobExecutionResult>();
        foreach (var job in config.Items.Where(job => job.Enabled && job.Trigger.Type == "startup"))
        {
            startupResults.Add(await RunAndReportAsync(job, cancellationToken));
        }

        foreach (var job in config.Items.Where(job => job.Enabled && IsScheduledTrigger(job.Trigger)))
        {
            StartScheduledJob(job);
        }

        return startupResults;
    }

    public Task<JobExecutionResult> RunManualAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var job = GetJob(jobId);
        return RunAndReportAsync(job, cancellationToken);
    }

    public bool StartIntervalJob(string jobId)
    {
        return StartScheduledJob(GetJob(jobId));
    }

    public bool StartIntervalJob(JobConfig job)
    {
        return StartScheduledJob(job);
    }

    public bool StartScheduledJob(string jobId)
    {
        return StartScheduledJob(GetJob(jobId));
    }

    public bool StartScheduledJob(JobConfig job)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!job.Enabled || !IsScheduledTrigger(job.Trigger))
        {
            return false;
        }

        var source = new CancellationTokenSource();
        lock (_sync)
        {
            if (_scheduledTokens.ContainsKey(job.Id))
            {
                source.Dispose();
                return false;
            }

            _scheduledTokens[job.Id] = source;
        }

        try
        {
            _ = job.Trigger.Type == "cron"
                ? RunCronLoopAsync(job, CronSchedule.Parse(job.Trigger.Expression), source.Token)
                : RunIntervalLoopAsync(job, ReadInterval(job), source.Token);
            return true;
        }
        catch
        {
            lock (_sync)
            {
                _scheduledTokens.Remove(job.Id);
            }

            source.Dispose();
            throw;
        }
    }

    public bool StopJob(string jobId)
    {
        CancellationTokenSource? source;
        lock (_sync)
        {
            if (!_scheduledTokens.Remove(jobId, out source))
            {
                return false;
            }
        }

        source.Cancel();
        source.Dispose();
        return true;
    }

    public bool IsJobRunning(string jobId)
    {
        lock (_sync)
        {
            return _scheduledTokens.ContainsKey(jobId);
        }
    }

    public bool IsJobExecuting(string jobId)
    {
        lock (_sync)
        {
            return _activeRuns.Contains(jobId);
        }
    }

    public bool HasActiveRuns
    {
        get
        {
            lock (_sync)
            {
                return _activeRuns.Count > 0;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAll();
        _disposed = true;
    }

    private JobConfig GetJob(string jobId)
    {
        lock (_sync)
        {
            if (_jobs.TryGetValue(jobId, out var job))
            {
                return job;
            }
        }

        throw new InvalidOperationException($"Job '{jobId}' is not configured.");
    }

    private async Task RunIntervalLoopAsync(JobConfig job, TimeSpan every, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _delayAsync(every, cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    await RunAndReportAsync(job, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunCronLoopAsync(JobConfig job, CronSchedule schedule, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _delayAsync(schedule.GetDelay(DateTimeOffset.Now), cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    await RunAndReportAsync(job, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<JobExecutionResult> RunAndReportAsync(JobConfig job, CancellationToken cancellationToken)
    {
        if (!TryBeginRun(job.Id))
        {
            var busy = new JobExecutionResult(job.Id, Succeeded: false, Error: "Job is already running.");
            JobCompleted?.Invoke(new JobRunRecord(job, busy, DateTimeOffset.UtcNow));
            return busy;
        }

        JobExecutionResult result;
        try
        {
            JobStarted?.Invoke(job);
            result = await _runner.RunOnceAsync(job, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = new JobExecutionResult(job.Id, Succeeded: false, Error: ex.Message);
        }
        finally
        {
            EndRun(job.Id);
        }

        JobCompleted?.Invoke(new JobRunRecord(job, result, DateTimeOffset.UtcNow));
        return result;
    }

    private bool TryBeginRun(string jobId)
    {
        lock (_sync)
        {
            return _activeRuns.Add(jobId);
        }
    }

    private void EndRun(string jobId)
    {
        lock (_sync)
        {
            _activeRuns.Remove(jobId);
        }
    }

    private void StopAll()
    {
        List<CancellationTokenSource> sources;
        lock (_sync)
        {
            sources = _scheduledTokens.Values.ToList();
            _scheduledTokens.Clear();
        }

        foreach (var source in sources)
        {
            source.Cancel();
            source.Dispose();
        }
    }

    private static bool IsScheduledTrigger(JobTriggerConfig trigger)
    {
        return trigger.Type is "interval" or "cron";
    }

    private static TimeSpan ReadInterval(JobConfig job)
    {
        var every = job.Trigger.Every ?? throw new InvalidOperationException("Interval trigger requires an interval.");
        return every > TimeSpan.Zero
            ? every
            : throw new InvalidOperationException("Interval trigger must be greater than zero.");
    }
}

internal sealed class CronSchedule
{
    private readonly IReadOnlySet<int> _seconds;
    private readonly IReadOnlySet<int> _minutes;
    private readonly IReadOnlySet<int> _hours;
    private readonly IReadOnlySet<int> _daysOfMonth;
    private readonly IReadOnlySet<int> _months;
    private readonly IReadOnlySet<int> _daysOfWeek;

    private CronSchedule(
        IReadOnlySet<int> seconds,
        IReadOnlySet<int> minutes,
        IReadOnlySet<int> hours,
        IReadOnlySet<int> daysOfMonth,
        IReadOnlySet<int> months,
        IReadOnlySet<int> daysOfWeek)
    {
        _seconds = seconds;
        _minutes = minutes;
        _hours = hours;
        _daysOfMonth = daysOfMonth;
        _months = months;
        _daysOfWeek = daysOfWeek;
    }

    public static CronSchedule Parse(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new InvalidOperationException("Cron trigger requires an expression.");
        }

        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return fields.Length switch
        {
            5 => new CronSchedule(
                new HashSet<int> { 0 },
                ParseField(fields[0], 0, 59),
                ParseField(fields[1], 0, 23),
                ParseField(fields[2], 1, 31),
                ParseField(fields[3], 1, 12),
                ParseField(fields[4], 0, 7, normalizeSunday: true)),
            6 => new CronSchedule(
                ParseField(fields[0], 0, 59),
                ParseField(fields[1], 0, 59),
                ParseField(fields[2], 0, 23),
                ParseField(fields[3], 1, 31),
                ParseField(fields[4], 1, 12),
                ParseField(fields[5], 0, 7, normalizeSunday: true)),
            _ => throw new FormatException($"Cron expression '{expression}' must have 5 or 6 fields.")
        };
    }

    public TimeSpan GetDelay(DateTimeOffset now)
    {
        var candidate = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            now.Hour,
            now.Minute,
            now.Second,
            now.Offset).AddSeconds(1);

        for (var i = 0; i < 366 * 24 * 60 * 60; i++)
        {
            if (Matches(candidate))
            {
                return candidate - now;
            }

            candidate = candidate.AddSeconds(1);
        }

        throw new InvalidOperationException("Cron expression did not produce a run time within one year.");
    }

    private bool Matches(DateTimeOffset value)
    {
        return _seconds.Contains(value.Second) &&
               _minutes.Contains(value.Minute) &&
               _hours.Contains(value.Hour) &&
               _daysOfMonth.Contains(value.Day) &&
               _months.Contains(value.Month) &&
               _daysOfWeek.Contains((int)value.DayOfWeek);
    }

    private static IReadOnlySet<int> ParseField(
        string field,
        int min,
        int max,
        bool normalizeSunday = false)
    {
        var values = new HashSet<int>();
        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddPart(values, part, min, max, normalizeSunday);
        }

        if (values.Count == 0)
        {
            throw new FormatException($"Cron field '{field}' does not contain any values.");
        }

        return values;
    }

    private static void AddPart(HashSet<int> values, string part, int min, int max, bool normalizeSunday)
    {
        var step = 1;
        var range = part;
        var slash = part.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            range = part[..slash];
            if (!int.TryParse(part[(slash + 1)..], out step) || step <= 0)
            {
                throw new FormatException($"Cron step '{part}' must be a positive integer.");
            }
        }

        var (start, end) = range switch
        {
            "*" => (min, max),
            _ when range.Contains('-', StringComparison.Ordinal) => ReadRange(range, min, max),
            _ => (ReadNumber(range, min, max, normalizeSunday), ReadNumber(range, min, max, normalizeSunday))
        };

        if (start > end)
        {
            throw new FormatException($"Cron range '{range}' must start before it ends.");
        }

        for (var value = start; value <= end; value += step)
        {
            values.Add(normalizeSunday && value == 7 ? 0 : value);
        }
    }

    private static (int Start, int End) ReadRange(string range, int min, int max)
    {
        var parts = range.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new FormatException($"Cron range '{range}' is not valid.");
        }

        return (ReadNumber(parts[0], min, max), ReadNumber(parts[1], min, max));
    }

    private static int ReadNumber(string raw, int min, int max, bool normalizeSunday = false)
    {
        if (!int.TryParse(raw, out var value))
        {
            throw new FormatException($"Cron value '{raw}' is not a number.");
        }

        var normalized = normalizeSunday && value == 7 ? 0 : value;
        if (normalized < min || normalized > (normalizeSunday ? Math.Min(max, 6) : max))
        {
            throw new FormatException($"Cron value '{raw}' must be between {min} and {max}.");
        }

        return normalized;
    }
}
