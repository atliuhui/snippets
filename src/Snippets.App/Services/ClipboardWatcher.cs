using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Snippets.Core.Clips;

namespace Snippets.App.Services;

public sealed class ClipboardWatcher : IDisposable
{
    private static readonly DataFormat<byte[]> HtmlWindows =
        DataFormat.CreateBytesPlatformFormat("HTML Format");
    private static readonly DataFormat<byte[]> HtmlMime =
        DataFormat.CreateBytesPlatformFormat("text/html");

    private readonly IClipboard _clipboard;
    private readonly ClipStore _store;
    private byte[] _lastHash = [];
    private uint _lastSequence;
    private bool _polling;
    private bool _primed;
    private bool _isRunning;

    public ClipboardWatcher(IClipboard clipboard, ClipStore store)
    {
        _clipboard = clipboard;
        _store = store;
    }

    public bool IsRunning => _isRunning;

    public event Action<ClipItem>? ItemSaved;

    public void Start() => _isRunning = true;

    public void Stop() => _isRunning = false;

    public void Dispose() => Stop();

    public async Task PollAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        if (_polling)
        {
            return;
        }

        _polling = true;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var sequence = GetClipboardSequenceNumber();
                if (sequence == _lastSequence)
                {
                    return;
                }

                _lastSequence = sequence;
            }

            await PollCoreAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Clipboard polling failed.", ex);
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task PollCoreAsync()
    {
        var (payload, kind) = await ReadCurrentAsync();
        if (payload is null)
        {
            return;
        }

        var hash = HashWithKind(kind, payload);
        if (hash.AsSpan().SequenceEqual(_lastHash))
        {
            return;
        }

        _lastHash = hash;

        if (!_primed)
        {
            _primed = true;
            return;
        }

        var item = _store.Save(payload, kind, DateTime.UtcNow);
        _store.PruneAutoSave();
        ItemSaved?.Invoke(item);
    }

    private async Task<(byte[]? Payload, ClipKind Kind)> ReadCurrentAsync()
    {
        try
        {
            var files = await _clipboard.TryGetFilesAsync();
            if (files is not null)
            {
                var paths = files
                    .Select(file => file.TryGetLocalPath())
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Cast<string>()
                    .ToArray();
                if (paths.Length > 0)
                {
                    return (Encoding.UTF8.GetBytes(string.Join("\n", paths)), ClipKind.FileList);
                }
            }
        }
        catch
        {
        }

        try
        {
            var bitmap = await _clipboard.TryGetBitmapAsync();
            if (bitmap is not null)
            {
                using var stream = new MemoryStream();
#pragma warning disable CS0618
                bitmap.Save(stream);
#pragma warning restore CS0618
                return (stream.ToArray(), ClipKind.Image);
            }
        }
        catch
        {
        }

        foreach (var format in new[] { HtmlWindows, HtmlMime })
        {
            try
            {
                var bytes = await _clipboard.TryGetValueAsync(format);
                if (bytes is { Length: > 0 })
                {
                    var stripped = StripCfHtmlHeader(bytes);
                    if (stripped.Length > 0)
                    {
                        return (stripped, ClipKind.Html);
                    }
                }
            }
            catch
            {
            }
        }

        try
        {
            var text = await _clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                return (Encoding.UTF8.GetBytes(text), ClipKind.Text);
            }
        }
        catch
        {
        }

        return (null, ClipKind.Unknown);
    }

    private static byte[] HashWithKind(ClipKind kind, byte[] payload)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> prefix = stackalloc byte[1] { (byte)kind };
        hasher.AppendData(prefix);
        hasher.AppendData(payload);
        return hasher.GetHashAndReset();
    }

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private static byte[] StripCfHtmlHeader(byte[] bytes)
    {
        if (bytes.Length < 8)
        {
            return bytes;
        }

        var text = DecodeHtmlText(bytes);
        if (!text.Contains("Version:", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("StartHTML:", StringComparison.OrdinalIgnoreCase))
        {
            return bytes;
        }

        var start = ParseOffset(text, "StartFragment:");
        var end = ParseOffset(text, "EndFragment:");
        if (start >= 0 && end > start)
        {
            var fragment = text.Substring(start, end - start);
            return Encoding.UTF8.GetBytes(fragment);
        }

        return Encoding.UTF8.GetBytes(text);
    }

    private static string DecodeHtmlText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return Encoding.Unicode.GetString(bytes);
        }
    }

    private static int ParseOffset(string text, string key)
    {
        var index = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return -1;
        }

        var start = index + key.Length;
        var end = start;
        while (end < text.Length && char.IsDigit(text[end]))
        {
            end++;
        }

        return end > start && int.TryParse(text.Substring(start, end - start), out var value)
            ? value
            : -1;
    }
}
