using Snippets.Core.Clips;

namespace Snippets.Tests;

public sealed class ClipCaptureServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SnippetsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CaptureAsync_skips_consecutive_duplicates()
    {
        var service = CreateService(maxAutoSave: 10);

        var first = await service.CaptureAsync(ClipPayload.FromText("same"));
        var second = await service.CaptureAsync(ClipPayload.FromText("same"));

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "AutoSave")));
    }

    [Fact]
    public async Task CaptureAsync_prunes_old_auto_save_files()
    {
        var service = CreateService(maxAutoSave: 2);

        await service.CaptureAsync(ClipPayload.FromText("one"), new DateTimeOffset(2026, 7, 24, 6, 0, 0, TimeSpan.Zero));
        await service.CaptureAsync(ClipPayload.FromText("two"), new DateTimeOffset(2026, 7, 24, 6, 1, 0, TimeSpan.Zero));
        await service.CaptureAsync(ClipPayload.FromText("three"), new DateTimeOffset(2026, 7, 24, 6, 2, 0, TimeSpan.Zero));

        Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(_root, "AutoSave")).Count());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ClipCaptureService CreateService(int maxAutoSave)
    {
        var store = new ClipStore(new ClipStoreOptions(
            Path.Combine(_root, "AutoSave"),
            Path.Combine(_root, "Favorites"),
            maxAutoSave));

        return new ClipCaptureService(store, TimeSpan.FromMinutes(10));
    }
}
