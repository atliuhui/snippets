namespace Snippets.Core.Notes;

public sealed record NoteDocument(string Name, string Path, string Content, DateTimeOffset Updated);

public sealed class NoteService
{
    private readonly string _draftsDirectory;

    public NoteService(string draftsDirectory)
    {
        if (string.IsNullOrWhiteSpace(draftsDirectory))
        {
            throw new ArgumentException("Notes drafts directory is required.", nameof(draftsDirectory));
        }

        _draftsDirectory = draftsDirectory;
    }

    public async Task<NoteDocument> SaveAsync(string name, string content, CancellationToken cancellationToken = default)
    {
        var path = GetDraftPath(name);
        Directory.CreateDirectory(_draftsDirectory);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return await ReadAsync(name, cancellationToken);
    }

    public async Task<NoteDocument> ReadAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = GetDraftPath(name);
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var updated = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        return new NoteDocument(Path.GetFileName(path), path, content, updated);
    }

    public IReadOnlyList<NoteDocument> List()
    {
        Directory.CreateDirectory(_draftsDirectory);

        return Directory.EnumerateFiles(_draftsDirectory, "*.md")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new NoteDocument(
                Path.GetFileName(path),
                path,
                File.ReadAllText(path),
                new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)))
            .ToList();
    }

    public void Delete(string name)
    {
        var path = GetDraftPath(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetDraftPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Note name is required.", nameof(name));
        }

        var fileName = Path.GetFileName(name);
        if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".md";
        }

        return Path.Combine(_draftsDirectory, fileName);
    }
}
