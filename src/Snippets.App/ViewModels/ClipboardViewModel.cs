using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Snippets.Core.Clips;

namespace Snippets.App.ViewModels;

public sealed class ClipboardViewModel : INotifyPropertyChanged
{
    private readonly ClipStore _store;
    private readonly IClipboard _clipboard;
    private bool _isPaused;

    public ClipboardViewModel(ClipStore store, IClipboard clipboard)
    {
        _store = store;
        _clipboard = clipboard;
        Items = [];
        Reload();
    }

    public ObservableCollection<ClipCardViewModel> Items { get; }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused == value)
            {
                return;
            }

            _isPaused = value;
            OnChanged();
            OnChanged(nameof(PauseButtonText));
            OnChanged(nameof(WatcherStatusText));
        }
    }

    public string PauseButtonText => IsPaused ? "Resume watcher" : "Pause watcher";
    public string WatcherStatusText => IsPaused ? "Paused" : "Watching";
    public string AutoSavePath => _store.AutoSavePath;

    public void Reload()
    {
        foreach (var old in Items)
        {
            old.Dispose();
        }

        Items.Clear();
        foreach (var item in _store.Enumerate())
        {
            Items.Add(new ClipCardViewModel(item, this));
        }
    }

    public void OnItemSaved(ClipItem item)
    {
        Items.Insert(0, new ClipCardViewModel(item, this));
        TrimVisibleAutoSaveOverflow();
    }

    internal void Delete(ClipCardViewModel card)
    {
        _store.Delete(card.Item);
        Items.Remove(card);
        card.Dispose();
    }

    internal void TogglePin(ClipCardViewModel card)
    {
        var updated = card.Item.IsPinned ? _store.Unpin(card.Item) : _store.Pin(card.Item);
        card.UpdateItem(updated);
    }

    internal async Task CopyBackAsync(ClipCardViewModel card)
    {
        try
        {
            var item = card.Item;
            if (item.Kind is ClipKind.Text or ClipKind.Html or ClipKind.FileList)
            {
                var text = await File.ReadAllTextAsync(item.FilePath, Encoding.UTF8);
                await _clipboard.SetTextAsync(text);
            }
            else if (item.Kind == ClipKind.Image)
            {
                await using var stream = File.OpenRead(item.FilePath);
                var bitmap = new Bitmap(stream);
                await _clipboard.SetBitmapAsync(bitmap);
            }
        }
        catch
        {
        }
    }

    internal void Reveal(ClipCardViewModel card)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", $"/select,\"{card.Item.FilePath}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", $"-R \"{card.Item.FilePath}\"");
            }
            else
            {
                Process.Start("xdg-open", $"\"{Path.GetDirectoryName(card.Item.FilePath)}\"");
            }
        }
        catch
        {
        }
    }

    private void TrimVisibleAutoSaveOverflow()
    {
        var extra = Items.Count(item => !item.IsPinned) - _store.MaxAutoSave;
        if (extra <= 0)
        {
            return;
        }

        for (var index = Items.Count - 1; index >= 0 && extra > 0; index--)
        {
            if (!Items[index].IsPinned)
            {
                var evicted = Items[index];
                Items.RemoveAt(index);
                evicted.Dispose();
                extra--;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class ClipCardViewModel : INotifyPropertyChanged, IDisposable
{
    private const int ThumbnailHeight = 64;
    private const int TextPreviewChars = 200;

    private readonly ClipboardViewModel _parent;
    private ClipItem _item;
    private Bitmap? _thumbnail;
    private string? _preview;
    private bool _previewLoaded;
    private bool _isPendingDelete;

    public ClipCardViewModel(ClipItem item, ClipboardViewModel parent)
    {
        _item = item;
        _parent = parent;
        LoadThumbnailIfImage();
    }

    public ClipItem Item => _item;
    public string TimestampDisplay => _item.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
    public string KindDisplay => _item.Kind.DisplayName();
    public string SizeDisplay => FormatBytes(_item.SizeBytes);
    public string HeadlineDisplay => $"{KindDisplay} · {SizeDisplay}";
    public bool IsPinned => _item.IsPinned;
    public bool HasThumbnail => _thumbnail is not null;
    public Bitmap? Thumbnail => _thumbnail;
    public string PinButtonText => IsPinned ? "Unpin" : "Pin";

    public bool IsPendingDelete
    {
        get => _isPendingDelete;
        set
        {
            if (_isPendingDelete == value)
            {
                return;
            }

            _isPendingDelete = value;
            OnChanged(nameof(IsPendingDelete));
            OnChanged(nameof(ShowDeleteIdle));
            OnChanged(nameof(ShowDeleteArmed));
        }
    }

    public bool ShowDeleteIdle => !_isPendingDelete;
    public bool ShowDeleteArmed => _isPendingDelete;

    public string Preview
    {
        get
        {
            if (!_previewLoaded)
            {
                _previewLoaded = true;
                _preview = LoadPreview();
            }

            return _preview ?? string.Empty;
        }
    }

    internal void UpdateItem(ClipItem updated)
    {
        _item = updated;
        OnChanged(nameof(Item));
        OnChanged(nameof(IsPinned));
        OnChanged(nameof(PinButtonText));
    }

    public void Delete() => _parent.Delete(this);
    public void TogglePin() => _parent.TogglePin(this);
    public async void CopyBack() => await _parent.CopyBackAsync(this);
    public void Reveal() => _parent.Reveal(this);

    public void Dispose()
    {
        _thumbnail?.Dispose();
        _thumbnail = null;
    }

    private void LoadThumbnailIfImage()
    {
        if (_item.Kind != ClipKind.Image)
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(_item.FilePath);
            _thumbnail = Bitmap.DecodeToHeight(stream, ThumbnailHeight);
        }
        catch
        {
            _thumbnail = null;
        }
    }

    private string LoadPreview()
    {
        try
        {
            return _item.Kind switch
            {
                ClipKind.Text or ClipKind.Html or ClipKind.FileList => ReadHead(_item.FilePath),
                ClipKind.Image => "[image]",
                _ => string.Empty,
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadHead(string path)
    {
        var buffer = new char[TextPreviewChars];
        using var reader = new StreamReader(path, Encoding.UTF8);
        var count = reader.Read(buffer, 0, buffer.Length);
        var text = new string(buffer, 0, count).Replace('\r', ' ').Replace('\n', ' ');
        return count >= buffer.Length ? text + "..." : text;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:0.#} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):0.##} MB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged(string name)
    {
        Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
    }
}
