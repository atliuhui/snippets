using Snippets.Core.Notes;

namespace Snippets.Tests;

public sealed class QuickCopyExtractorTests
{
    [Fact]
    public void Extract_returns_nested_quick_copy_items_in_source_order()
    {
        const string note = """
            # Profile

            <section data-copy-id="profile.full" data-copy-label="Full profile">
              Name: <span data-copy-id="profile.name" data-copy-label="Name">John Doe</span>
              Phone: <span data-copy-id="profile.phone">13800000000</span>
            </section>
            """;
        var updated = new DateTimeOffset(2026, 7, 24, 6, 0, 0, TimeSpan.Zero);

        var result = new QuickCopyExtractor().Extract(note, "profile.md", updated);

        Assert.False(result.HasIssues);
        Assert.Equal(["profile.full", "profile.name", "profile.phone"], result.Items.Select(item => item.Id).ToArray());
        Assert.Equal("Name: John Doe Phone: 13800000000", result.Items[0].Value);
        Assert.Equal("Name", result.Items[1].Label);
        Assert.Equal("13800000000", result.Items[2].Value);
    }

    [Fact]
    public void Extract_reports_duplicate_ids_and_unclosed_tags()
    {
        const string note = """
            <span data-copy-id="same">One</span>
            <span data-copy-id="same">Two</span>
            <section data-copy-id="open">
            """;

        var result = new QuickCopyExtractor().Extract(note, "broken.md", DateTimeOffset.UtcNow);

        Assert.Contains(result.Issues, issue => issue.Code == "duplicate-id");
        Assert.Contains(result.Issues, issue => issue.Code == "unclosed-tag");
    }
}
