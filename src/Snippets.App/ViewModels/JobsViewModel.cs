using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Snippets.Core.Config;
using Snippets.Core.Jobs;

namespace Snippets.App.ViewModels;

public sealed class JobsViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<string, JobCardViewModel> _jobsById;
    private bool _jobsEnabled;
    private string _refreshStatusText = string.Empty;

    public JobsViewModel(JobsConfig config)
    {
        _jobsEnabled = config.Enabled;
        Jobs = [];
        _jobsById = new Dictionary<string, JobCardViewModel>(StringComparer.Ordinal);
        ReplaceJobs(config);
    }

    public bool JobsEnabled => _jobsEnabled;
    public string SummaryText => JobsEnabled ? "Jobs runner is enabled" : "Jobs runner is disabled";
    public ObservableCollection<JobCardViewModel> Jobs { get; }
    public bool HasRefreshStatus => !string.IsNullOrWhiteSpace(RefreshStatusText);

    public string RefreshStatusText
    {
        get => _refreshStatusText;
        set
        {
            if (_refreshStatusText == value)
            {
                return;
            }

            _refreshStatusText = value;
            OnChanged();
            OnChanged(nameof(HasRefreshStatus));
        }
    }

    public void ReplaceJobs(JobsConfig config)
    {
        _jobsEnabled = config.Enabled;
        _jobsById.Clear();
        Jobs.Clear();
        foreach (var job in config.Items)
        {
            var viewModel = new JobCardViewModel(job);
            _jobsById[job.Id] = viewModel;
            Jobs.Add(viewModel);
        }

        OnChanged(nameof(JobsEnabled));
        OnChanged(nameof(SummaryText));
    }

    public void OnJobCompleted(JobRunRecord record)
    {
        if (_jobsById.TryGetValue(record.Job.Id, out var job))
        {
            job.ApplyRunRecord(record);
        }
    }

    public void SetJobRunning(string jobId, bool running)
    {
        if (_jobsById.TryGetValue(jobId, out var job))
        {
            job.IsRunning = running;
        }
    }

    public void SetJobBusy(string jobId, bool busy)
    {
        if (_jobsById.TryGetValue(jobId, out var job))
        {
            job.IsBusy = busy;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class JobCardViewModel : INotifyPropertyChanged
{
    private readonly JobConfig _job;
    private bool _isRunning;
    private bool _isBusy;
    private string _lastResultText = "Not run yet";
    private string _lastFinishedText = "No runs recorded";
    private string _lastDetailText = "The job has not produced output yet.";
    private bool _lastRunSucceeded;
    private bool _hasRun;

    public JobCardViewModel(JobConfig job)
    {
        _job = job;
    }

    public string Id => _job.Id;
    public string Name => _job.Name;
    public bool Enabled => _job.Enabled;
    public bool CanToggle => IsPeriodicTrigger(_job.Trigger);
    public bool IsToggleEnabled => Enabled && CanToggle;
    public bool CanRunNow => _job.Enabled && !IsPeriodicTrigger(_job.Trigger);
    public bool IsRunNowEnabled => CanRunNow && !IsBusy;
    public string TriggerDisplay => FormatTrigger(_job.Trigger);
    public string ActionDisplay => FormatAction(_job.Action);
    public string StateText => !Enabled
        ? "Disabled"
        : IsBusy
            ? "Running now"
            : CanToggle
                ? IsRunning ? ScheduledStateText : "Paused"
                : "Idle";
    public string ToggleButtonText => IsRunning ? "Pause" : "Resume";
    public string RunButtonText => IsBusy ? "Running..." : "Run now";
    private string ScheduledStateText => _job.Trigger.Type == "interval" ? "Watching" : "Scheduled";
    public bool HasActiveStatus => Enabled && (IsRunning || IsBusy);
    public bool HasPausedStatus => Enabled && CanToggle && !IsRunning && !IsBusy;
    public bool HasNeutralStatus => !HasActiveStatus && !HasPausedStatus;
    public string LastResultText => _lastResultText;
    public string LastFinishedText => _lastFinishedText;
    public string LastDetailText => _lastDetailText;
    public bool LastRunSucceeded => _lastRunSucceeded;
    public bool HasRun => _hasRun;
    public bool LastRunFailed => HasRun && !LastRunSucceeded;

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            OnChanged();
            OnChanged(nameof(StateText));
            OnChanged(nameof(ToggleButtonText));
            OnChanged(nameof(IsToggleEnabled));
            OnChanged(nameof(HasActiveStatus));
            OnChanged(nameof(HasPausedStatus));
            OnChanged(nameof(HasNeutralStatus));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnChanged();
            OnChanged(nameof(StateText));
            OnChanged(nameof(RunButtonText));
            OnChanged(nameof(IsRunNowEnabled));
            OnChanged(nameof(HasActiveStatus));
            OnChanged(nameof(HasPausedStatus));
            OnChanged(nameof(HasNeutralStatus));
        }
    }

    public void ApplyRunRecord(JobRunRecord record)
    {
        _hasRun = true;
        _lastRunSucceeded = record.Result.Succeeded;
        _lastResultText = record.Result.Succeeded ? "Succeeded" : "Failed";
        _lastFinishedText = record.FinishedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        _lastDetailText = FormatResultDetail(record.Result);

        OnChanged(nameof(HasRun));
        OnChanged(nameof(LastRunSucceeded));
        OnChanged(nameof(LastRunFailed));
        OnChanged(nameof(LastResultText));
        OnChanged(nameof(LastFinishedText));
        OnChanged(nameof(LastDetailText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static string FormatTrigger(JobTriggerConfig trigger)
    {
        return trigger.Type switch
        {
            "startup" => "startup",
            "manual" => "manual",
            "interval" => $"interval every {FormatDuration(trigger.Every)}",
            "cron" => $"cron {trigger.Expression}",
            _ => trigger.Type
        };
    }

    private static bool IsPeriodicTrigger(JobTriggerConfig trigger)
    {
        return trigger.Type is "interval" or "cron";
    }

    private static string FormatAction(JobActionConfig action)
    {
        return action.Type switch
        {
            "tool" => $"tool {action.Name}",
            "command" => FormatCommand(action),
            _ => action.Type
        };
    }

    private static string FormatCommand(JobActionConfig action)
    {
        var args = action.CommandArgs is { Count: > 0 }
            ? " " + string.Join(" ", action.CommandArgs)
            : string.Empty;
        var timeout = action.Timeout is { } value
            ? $" (timeout {FormatDuration(value)})"
            : string.Empty;
        return $"command {action.Command}{args}{timeout}";
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "?";
        }

        if (duration.Value.TotalSeconds < 60)
        {
            return $"{duration.Value.TotalSeconds:0.#}s";
        }

        if (duration.Value.TotalMinutes < 60)
        {
            return $"{duration.Value.TotalMinutes:0.#}m";
        }

        return $"{duration.Value.TotalHours:0.#}h";
    }

    private static string FormatResultDetail(JobExecutionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return result.ExitCode is null
                ? result.Error.Trim()
                : $"exit {result.ExitCode}: {result.Error.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            return result.Output.Trim();
        }

        return result.ExitCode is null
            ? "Completed without output."
            : $"exit {result.ExitCode}";
    }
}
