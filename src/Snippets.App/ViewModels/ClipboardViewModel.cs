using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Snippets.Core.Clips;

namespace Snippets.App.ViewModels;

public sealed class ClipboardViewModel : INotifyPropertyChanged
{
    private readonly ClipStore _store;
    private readonly IClipboard _clipboard;

    public ClipboardViewModel(ClipStore store, IClipboard clipboard)
    {
        _store = store;
        _clipboard = clipboard;
        Items = [];
        Reload();
    }

    public ObservableCollection<ClipCardViewModel> Items { get; }

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
                await _clipboard.SetTextAsync(item.Kind == ClipKind.Html ? ExtractPlainText(text) : text);
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

    private static string ExtractPlainText(string rawHtml)
    {
        if (string.IsNullOrWhiteSpace(rawHtml))
        {
            return string.Empty;
        }

        var normalized = rawHtml
            .Replace("\uFEFF", string.Empty)
            .Replace("\0", string.Empty)
            .Replace("&nbsp;", " ")
            .Replace("&#160;", " ")
            .Replace("&#xA0;", " ");

        normalized = Regex.Replace(normalized, "<!--.*?-->", string.Empty, RegexOptions.CultureInvariant | RegexOptions.Singleline);
        normalized = Regex.Replace(normalized, "(?is)<br\\s*/?>|</?(div|p|li|ul|ol|tr|table|h[1-6]|blockquote|pre)[^>]*>", "\n");
        normalized = Regex.Replace(normalized, "(?is)<(span|b|strong|i|em|u|font|code|a)[^>]*>", string.Empty);
        normalized = Regex.Replace(normalized, "<[^>]+>", string.Empty, RegexOptions.CultureInvariant | RegexOptions.Singleline);

        var decoded = WebUtility.HtmlDecode(normalized).Replace('\u00A0', ' ');
        var lines = decoded.Replace("\r", "\n").Split('\n');
        var cleaned = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            cleaned.Add(trimmed);
        }

        return string.Join("\n", cleaned);
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
    public bool IsHtml => _item.Kind == ClipKind.Html;
    public bool HasThumbnail => _thumbnail is not null;
    public Bitmap? Thumbnail => _thumbnail;
    public string PinButtonText => IsPinned ? "Unpin" : "Pin";
    public string CopyButtonText => IsHtml ? "Copy text" : "Copy";

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
                ClipKind.Text or ClipKind.FileList => ReadHead(_item.FilePath),
                ClipKind.Html => ExtractVisibleText(ReadHead(_item.FilePath)),
                ClipKind.Image => string.Empty,
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
        var text = new string(buffer, 0, count)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        var lines = text.Split('\n');
        if (lines.Length > 1)
        {
            var joined = string.Join("\n", lines.Take(3));
            return joined + (count >= buffer.Length ? "\n..." : string.Empty);
        }

        var trimmed = text.Trim();
        return count >= buffer.Length ? trimmed + "..." : trimmed;
    }

    private static string ExtractVisibleText(string source)
    {
        var normalized = Regex.Replace(
            source,
            "(?is)<!--.*?-->|<br\\s*/?>|</?(div|p|li|ul|ol|tr|table|h[1-6]|blockquote|pre)[^>]*>",
            "\n");
        var noTags = Regex.Replace(normalized, "<[^>]+>", string.Empty, RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var lines = WebUtility.HtmlDecode(noTags)
            .Replace('\u00A0', ' ')
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(3);

        return string.Join("\n", lines);
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
