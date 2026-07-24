using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
    private readonly DispatcherTimer _timer;
    private byte[] _lastHash = [];
    private uint _lastSequence;
    private bool _polling;
    private bool _primed;

    public ClipboardWatcher(IClipboard clipboard, ClipStore store)
    {
        _clipboard = clipboard;
        _store = store;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTick);
    }

    public bool IsRunning => _timer.IsEnabled;

    public event Action<ClipItem>? ItemSaved;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void Dispose() => _timer.Stop();

    private async void OnTick(object? sender, EventArgs e)
    {
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

            await PollAsync();
        }
        catch
        {
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task PollAsync()
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
        if (bytes.Length < 8 || !StartsWithAscii(bytes, "Version:"))
        {
            return bytes;
        }

        var header = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
        var start = ParseOffset(header, "StartFragment:");
        var end = ParseOffset(header, "EndFragment:");
        if (start > 0 && end > start && end <= bytes.Length)
        {
            var length = end - start;
            var slice = new byte[length];
            Buffer.BlockCopy(bytes, start, slice, 0, length);
            return slice;
        }

        return bytes;
    }

    private static bool StartsWithAscii(byte[] bytes, string prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != (byte)prefix[index])
            {
                return false;
            }
        }

        return true;
    }

    private static int ParseOffset(string header, string key)
    {
        var index = header.IndexOf(key, StringComparison.Ordinal);
        if (index < 0)
        {
            return -1;
        }

        var start = index + key.Length;
        var end = start;
        while (end < header.Length && char.IsDigit(header[end]))
        {
            end++;
        }

        return end > start && int.TryParse(header.AsSpan(start, end - start), out var value)
            ? value
            : -1;
    }
}
