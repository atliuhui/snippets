using System.Security.Cryptography;

namespace Snippets.Core.Clips;

public sealed class ClipCaptureService
{
    private readonly ClipStore _store;
    private readonly TimeSpan? _dedupeCacheWindow;
    private readonly Dictionary<string, DateTimeOffset> _recentHashes = [];
    private string? _lastHash;

    public ClipCaptureService(ClipStore store, TimeSpan? dedupeCacheWindow = null)
    {
        _store = store;
        _dedupeCacheWindow = dedupeCacheWindow;
    }

    public async Task<ClipItem?> CaptureAsync(ClipPayload payload, DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        var capturedAt = now ?? DateTimeOffset.Now;
        var hash = Convert.ToHexString(SHA256.HashData(payload.Content)).ToLowerInvariant();

        if (hash == _lastHash || IsInsideCacheWindow(hash, capturedAt))
        {
            return null;
        }

        var item = await _store.SaveAutoAsync(payload, capturedAt, cancellationToken);
        _lastHash = hash;
        Remember(hash, capturedAt);
        _store.PruneAutoSave();
        return item;
    }

    private bool IsInsideCacheWindow(string hash, DateTimeOffset now)
    {
        if (_dedupeCacheWindow is null)
        {
            return false;
        }

        foreach (var staleHash in _recentHashes
            .Where(entry => now - entry.Value > _dedupeCacheWindow.Value)
            .Select(entry => entry.Key)
            .ToList())
        {
            _recentHashes.Remove(staleHash);
        }

        return _recentHashes.ContainsKey(hash);
    }

    private void Remember(string hash, DateTimeOffset capturedAt)
    {
        if (_dedupeCacheWindow is not null)
        {
            _recentHashes[hash] = capturedAt;
        }
    }
}
