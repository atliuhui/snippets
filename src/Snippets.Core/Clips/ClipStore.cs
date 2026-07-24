namespace Snippets.Core.Clips;

public sealed record ClipStoreOptions(string AutoSaveDirectory, string FavoritesDirectory, int MaxAutoSave)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AutoSaveDirectory))
        {
            throw new ArgumentException("Auto-save directory is required.", nameof(AutoSaveDirectory));
        }

        if (string.IsNullOrWhiteSpace(FavoritesDirectory))
        {
            throw new ArgumentException("Favorites directory is required.", nameof(FavoritesDirectory));
        }

        if (MaxAutoSave < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAutoSave), "Maximum auto-save count must be at least 1.");
        }
    }
}

public sealed class ClipStore
{
    private readonly ClipStoreOptions _options;

    public ClipStore(ClipStoreOptions options)
    {
        options.Validate();
        _options = options;
        AutoSavePath = options.AutoSaveDirectory;
        FavoritesPath = options.FavoritesDirectory;
        Directory.CreateDirectory(AutoSavePath);
        Directory.CreateDirectory(FavoritesPath);
    }

    public string AutoSavePath { get; }
    public string FavoritesPath { get; }
    public int MaxAutoSave => _options.MaxAutoSave;

    public IReadOnlyList<ClipItem> Enumerate()
    {
        var list = new List<ClipItem>();
        list.AddRange(ReadFolder(AutoSavePath, pinned: false));
        list.AddRange(ReadFolder(FavoritesPath, pinned: true));
        list.Sort(static (a, b) => string.CompareOrdinal(b.FileName, a.FileName));
        return list;
    }

    public ClipItem Save(byte[] payload, ClipKind kind, DateTime timestampUtc)
    {
        var name = FormatFileName(timestampUtc, kind);
        var full = Path.Combine(AutoSavePath, name);
        File.WriteAllBytes(full, payload);
        return new ClipItem(full, timestampUtc, kind, payload.LongLength, IsPinned: false);
    }

    public async Task<ClipItem> SaveAutoAsync(ClipPayload payload, DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        var timestampUtc = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
        var name = FormatFileName(timestampUtc, payload.Kind);
        var full = Path.Combine(AutoSavePath, name);
        await File.WriteAllBytesAsync(full, payload.Content, cancellationToken);
        return new ClipItem(full, timestampUtc, payload.Kind, payload.Content.LongLength, IsPinned: false);
    }

    public ClipItem Pin(ClipItem item)
    {
        if (item.IsPinned)
        {
            return item;
        }

        var destination = Path.Combine(FavoritesPath, item.FileName);
        File.Move(item.FilePath, destination, overwrite: true);
        return item with { FilePath = destination, IsPinned = true };
    }

    public ClipItem Unpin(ClipItem item)
    {
        if (!item.IsPinned)
        {
            return item;
        }

        var destination = Path.Combine(AutoSavePath, item.FileName);
        File.Move(item.FilePath, destination, overwrite: true);
        return item with { FilePath = destination, IsPinned = false };
    }

    public ClipItem Favorite(ClipItem item) => Pin(item);

    public void Delete(ClipItem item)
    {
        if (File.Exists(item.FilePath))
        {
            File.Delete(item.FilePath);
        }
    }

    public int PruneAutoSave()
    {
        if (MaxAutoSave <= 0)
        {
            return 0;
        }

        var files = new DirectoryInfo(AutoSavePath)
            .EnumerateFiles()
            .Where(file => ClipKindExtensions.FromExtension(file.Extension) != ClipKind.Unknown)
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToList();
        var excess = files.Count - MaxAutoSave;
        if (excess <= 0)
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in files.Take(excess))
        {
            try
            {
                file.Delete();
                deleted++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }

    private static IEnumerable<ClipItem> ReadFolder(string directory, bool pinned)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in new DirectoryInfo(directory).EnumerateFiles())
        {
            var kind = ClipKindExtensions.FromExtension(file.Extension);
            if (kind == ClipKind.Unknown)
            {
                continue;
            }

            var timestampUtc = TryParseTimestamp(Path.GetFileNameWithoutExtension(file.Name)) ?? file.CreationTimeUtc;
            yield return new ClipItem(file.FullName, timestampUtc, kind, file.Length, pinned);
        }
    }

    private static string FormatFileName(DateTime timestampUtc, ClipKind kind)
    {
        var stamp = timestampUtc.ToString("yyyy-MM-ddTHH-mm-ss-fff", System.Globalization.CultureInfo.InvariantCulture);
        return stamp + kind.Extension();
    }

    private static DateTime? TryParseTimestamp(string stem)
    {
        return DateTime.TryParseExact(
            stem,
            "yyyy-MM-ddTHH-mm-ss-fff",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    }
}
