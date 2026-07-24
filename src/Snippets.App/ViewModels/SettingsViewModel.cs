using System.ComponentModel;
using System.Runtime.CompilerServices;
using Snippets.Core.Config;

namespace Snippets.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private bool _isTrayEnabled;
    private bool _isStartWithSystemEnabled;
    private bool _isSaving;
    private string _statusText = string.Empty;

    public SettingsViewModel(SnippetsConfig config, string configPath)
    {
        _isTrayEnabled = config.App.Tray;
        _isStartWithSystemEnabled = config.App.StartWithSystem;
        ConfigPath = configPath;
    }

    public string ConfigPath { get; }
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    public bool IsTrayEnabled
    {
        get => _isTrayEnabled;
        private set
        {
            if (_isTrayEnabled == value)
            {
                return;
            }

            _isTrayEnabled = value;
            OnChanged();
        }
    }

    public bool IsStartWithSystemEnabled
    {
        get => _isStartWithSystemEnabled;
        private set
        {
            if (_isStartWithSystemEnabled == value)
            {
                return;
            }

            _isStartWithSystemEnabled = value;
            OnChanged();
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (_isSaving == value)
            {
                return;
            }

            _isSaving = value;
            OnChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnChanged();
            OnChanged(nameof(HasStatus));
        }
    }

    public async Task SetTrayEnabledAsync(bool enabled)
    {
        await SaveAsync(enabled, IsStartWithSystemEnabled);
    }

    public async Task SetStartWithSystemEnabledAsync(bool enabled)
    {
        await SaveAsync(IsTrayEnabled, enabled);
    }

    public void Apply(SnippetsConfig config)
    {
        IsTrayEnabled = config.App.Tray;
        IsStartWithSystemEnabled = config.App.StartWithSystem;
        StatusText = "Settings saved.";
    }

    public void Fail(string message)
    {
        StatusText = message;
    }

    private async Task SaveAsync(bool tray, bool startWithSystem)
    {
        if (IsSaving)
        {
            return;
        }

        IsSaving = true;
        try
        {
            var config = await App.UpdateAppSettingsAsync(tray, startWithSystem);
            Apply(config);
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            OnChanged(nameof(IsTrayEnabled));
            OnChanged(nameof(IsStartWithSystemEnabled));
        }
        finally
        {
            IsSaving = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
