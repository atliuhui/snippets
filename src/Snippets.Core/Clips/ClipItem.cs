using System.Text;

namespace Snippets.Core.Clips;

public enum ClipKind
{
    Text,
    Html,
    Image,
    FileList,
    Unknown,
}

public static class ClipKindExtensions
{
    public static string Extension(this ClipKind kind) => kind switch
    {
        ClipKind.Text => ".txt",
        ClipKind.Html => ".html",
        ClipKind.Image => ".png",
        ClipKind.FileList => ".csv",
        _ => ".bin",
    };

    public static ClipKind FromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".txt" => ClipKind.Text,
        ".html" => ClipKind.Html,
        ".png" => ClipKind.Image,
        ".csv" => ClipKind.FileList,
        _ => ClipKind.Unknown,
    };

    public static string DisplayName(this ClipKind kind) => kind switch
    {
        ClipKind.Text => "text/plain",
        ClipKind.Html => "text/html",
        ClipKind.Image => "image/png",
        ClipKind.FileList => "file list",
        _ => "unknown",
    };
}

public sealed record ClipPayload(ClipKind Kind, byte[] Content)
{
    public static ClipPayload FromText(string text)
    {
        return new ClipPayload(ClipKind.Text, Encoding.UTF8.GetBytes(text));
    }
}

public sealed record ClipItem(
    string FilePath,
    DateTime TimestampUtc,
    ClipKind Kind,
    long SizeBytes,
    bool IsPinned)
{
    public string FileName => Path.GetFileName(FilePath);
}
