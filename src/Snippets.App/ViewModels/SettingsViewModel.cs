using System.ComponentModel;
using System.Runtime.CompilerServices;
using Snippets.Core.Config;

namespace Snippets.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private bool _isCloseToTrayEnabled;
    private bool _isStartWithSystemEnabled;
    private bool _isSaving;
    private string _statusText = string.Empty;

    public SettingsViewModel(SnippetsConfig config, string configPath)
    {
        _isCloseToTrayEnabled = config.App.CloseToTray;
        _isStartWithSystemEnabled = config.App.StartWithSystem;
        ConfigPath = configPath;
    }

    public string ConfigPath { get; }
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    public bool IsCloseToTrayEnabled
    {
        get => _isCloseToTrayEnabled;
        private set
        {
            if (_isCloseToTrayEnabled == value)
            {
                return;
            }

            _isCloseToTrayEnabled = value;
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

    public async Task SetCloseToTrayEnabledAsync(bool enabled)
    {
        await SaveAsync(enabled, IsStartWithSystemEnabled);
    }

    public async Task SetStartWithSystemEnabledAsync(bool enabled)
    {
        await SaveAsync(IsCloseToTrayEnabled, enabled);
    }

    public void Apply(SnippetsConfig config)
    {
        IsCloseToTrayEnabled = config.App.CloseToTray;
        IsStartWithSystemEnabled = config.App.StartWithSystem;
        StatusText = "Settings saved.";
    }

    public void Fail(string message)
    {
        StatusText = message;
    }

    private async Task SaveAsync(bool closeToTray, bool startWithSystem)
    {
        if (IsSaving)
        {
            return;
        }

        IsSaving = true;
        try
        {
            var config = await App.UpdateAppSettingsAsync(closeToTray, startWithSystem);
            Apply(config);
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            OnChanged(nameof(IsCloseToTrayEnabled));
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
