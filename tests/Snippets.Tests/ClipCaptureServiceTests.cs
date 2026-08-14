using System.Reflection;
using Snippets.App.ViewModels;
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

    [Fact]
    public async Task SaveAutoAsync_names_files_using_local_time()
    {
        var store = new ClipStore(new ClipStoreOptions(
            Path.Combine(_root, "AutoSave"),
            Path.Combine(_root, "Favorites"),
            10));

        var now = DateTimeOffset.Now;
        var item = await store.SaveAutoAsync(ClipPayload.FromText("hello"), now);

        var expected = now.LocalDateTime.ToString("yyyy-MM-ddTHH-mm-ss-fff", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected + ".txt", Path.GetFileName(item.FilePath));
        Assert.Equal(now.UtcDateTime, item.TimestampUtc, TimeSpan.FromMilliseconds(1));

        var parsed = store.Enumerate().Single();
        Assert.Equal(item.FilePath, parsed.FilePath);
        Assert.True((parsed.TimestampUtc - now.UtcDateTime).Duration() <= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ExtractPlainText_preserves_yaml_like_indentation_for_html_clipboard_content()
    {
        var method = typeof(ClipboardViewModel)
            .GetMethod("ExtractPlainText", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var html = "<div><span>- </span><span>name</span><span>: </span><span>gradle</span></div>"
            + "<div><span>&#160; </span><span>tag</span><span>:</span></div>"
            + "<div><span>&#160; &#160; - </span><span>work</span></div>"
            + "<div><span>&#160; </span><span>scoop</span><span>: </span><span>main/gradle</span></div>"
            + "<!-- trailing comment -->";

        var result = (string?)method.Invoke(null, [html]);

        Assert.Equal("- name: gradle\n  tag:\n    - work\n  scoop: main/gradle", result);
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
