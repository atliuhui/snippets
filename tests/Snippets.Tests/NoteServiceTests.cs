using Snippets.Core.Notes;

namespace Snippets.Tests;

public sealed class NoteServiceTests
{
    [Fact]
    public async Task Save_list_read_and_delete_manage_markdown_drafts()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnippetsTests", Guid.NewGuid().ToString("N"));
        var notes = new NoteService(root);

        var saved = await notes.SaveAsync("profile", "# Profile");
        var listed = notes.List();
        var read = await notes.ReadAsync("profile.md");
        var tryRead = notes.TryRead("profile");

        Assert.Equal("profile.md", saved.Name);
        Assert.Equal(root, notes.DraftsDirectory);
        Assert.Single(listed);
        Assert.Equal("# Profile", read.Content);
        Assert.NotNull(tryRead);
        Assert.Equal("# Profile", tryRead.Content);

        notes.Delete("profile");

        Assert.Empty(notes.List());
        Assert.Null(notes.TryRead("quick.md"));
        Directory.Delete(root, recursive: true);
    }
}
