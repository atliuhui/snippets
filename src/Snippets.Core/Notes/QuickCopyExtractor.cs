using System.Net;
using System.Text.RegularExpressions;

namespace Snippets.Core.Notes;

public sealed record QuickCopyItem(
    string Id,
    string Label,
    string Value,
    QuickCopySource Source,
    DateTimeOffset Updated);

public sealed record QuickCopySource(string NotePath, int StartIndex, int EndIndex);

public sealed record QuickCopyIssue(string Code, string Message, int Index);

public sealed record QuickCopyResult(IReadOnlyList<QuickCopyItem> Items, IReadOnlyList<QuickCopyIssue> Issues)
{
    public bool HasIssues => Issues.Count > 0;
}

public sealed class QuickCopyExtractor
{
    private static readonly Regex TagRegex = new(
        @"<(?<closing>/)?(?<name>span|div|section)\b(?<attrs>[^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AttributeRegex = new(
        @"(?<name>[\w:-]+)\s*=\s*(""(?<value>[^""]*)""|'(?<value>[^']*)')",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AnyTagRegex = new(
        "<[^>]+>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public QuickCopyResult Extract(string markdown, string notePath, DateTimeOffset updated)
    {
        var stack = new Stack<OpenNode>();
        var items = new List<QuickCopyItem>();
        var issues = new List<QuickCopyIssue>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in TagRegex.Matches(markdown))
        {
            var name = match.Groups["name"].Value.ToLowerInvariant();
            var isClosing = match.Groups["closing"].Success && match.Groups["closing"].Value == "/";

            if (!isClosing)
            {
                stack.Push(new OpenNode(name, match.Index, match.Index + match.Length, ParseAttributes(match.Groups["attrs"].Value)));
                continue;
            }

            if (!TryClose(stack, name, out var node))
            {
                issues.Add(new QuickCopyIssue("invalid-closing-tag", $"Unexpected closing tag </{name}>.", match.Index));
                continue;
            }

            if (!node.Attributes.TryGetValue("data-copy-id", out var id) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!ids.Add(id))
            {
                issues.Add(new QuickCopyIssue("duplicate-id", $"Duplicate quick-copy id '{id}'.", node.StartIndex));
                continue;
            }

            var label = node.Attributes.TryGetValue("data-copy-label", out var explicitLabel) && !string.IsNullOrWhiteSpace(explicitLabel)
                ? explicitLabel
                : id;
            var innerMarkdown = markdown[node.ContentStartIndex..match.Index];
            var value = NormalizeInnerText(innerMarkdown);

            items.Add(new QuickCopyItem(
                id,
                WebUtility.HtmlDecode(label),
                value,
                new QuickCopySource(notePath, node.StartIndex, match.Index + match.Length),
                updated));
        }

        foreach (var node in stack)
        {
            issues.Add(new QuickCopyIssue("unclosed-tag", $"Unclosed <{node.Name}> tag.", node.StartIndex));
        }

        return new QuickCopyResult(items.OrderBy(item => item.Source.StartIndex).ToList(), issues);
    }

    private static bool TryClose(Stack<OpenNode> stack, string name, out OpenNode node)
    {
        if (stack.Count > 0 && stack.Peek().Name == name)
        {
            node = stack.Pop();
            return true;
        }

        node = default;
        return false;
    }

    private static Dictionary<string, string> ParseAttributes(string source)
    {
        return AttributeRegex.Matches(source)
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => WebUtility.HtmlDecode(match.Groups["value"].Value),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeInnerText(string source)
    {
        var withoutTags = AnyTagRegex.Replace(source, string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var lines = decoded
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => Regex.Replace(line, @"[ \t\f\v]+", " ").Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        return lines.Length > 1
            ? string.Join(Environment.NewLine, lines)
            : lines.FirstOrDefault() ?? string.Empty;
    }

    private readonly record struct OpenNode(
        string Name,
        int StartIndex,
        int ContentStartIndex,
        IReadOnlyDictionary<string, string> Attributes);
}
