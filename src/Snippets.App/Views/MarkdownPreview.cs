using System.Net;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Snippets.App.Views;

public sealed class MarkdownPreview : UserControl
{
    public static readonly StyledProperty<string> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownPreview, string>(nameof(Markdown), string.Empty);

    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly StackPanel _content = new() { Spacing = 8 };

    public MarkdownPreview()
    {
        Content = new ScrollViewer
        {
            Content = _content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
    }

    public string Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty)
        {
            Render();
        }
    }

    private void Render()
    {
        _content.Children.Clear();

        var lines = Markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        var code = new List<string>();
        var inCode = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                if (inCode)
                {
                    AddCodeBlock(code);
                    code.Clear();
                    inCode = false;
                }
                else
                {
                    inCode = true;
                }

                continue;
            }

            if (inCode)
            {
                code.Add(rawLine);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(paragraph);
                continue;
            }

            if (TryAddBlock(line, paragraph))
            {
                continue;
            }

            paragraph.Add(CleanText(line.Trim()));
        }

        FlushParagraph(paragraph);
        if (code.Count > 0)
        {
            AddCodeBlock(code);
        }
    }

    private bool TryAddBlock(string line, List<string> paragraph)
    {
        var trimmed = line.TrimStart();
        var headingLevel = CountHeadingLevel(trimmed);
        if (headingLevel > 0)
        {
            FlushParagraph(paragraph);
            AddTextBlock(CleanText(trimmed[headingLevel..].Trim()), FontWeight.SemiBold, HeadingSize(headingLevel));
            return true;
        }

        if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
            trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            FlushParagraph(paragraph);
            AddBullet(CleanText(trimmed[2..].Trim()));
            return true;
        }

        if (trimmed.StartsWith("> ", StringComparison.Ordinal))
        {
            FlushParagraph(paragraph);
            AddQuote(CleanText(trimmed[2..].Trim()));
            return true;
        }

        if (trimmed is "---" or "***" or "___")
        {
            FlushParagraph(paragraph);
            _content.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 4),
                Background = Brushes.Gray,
                Opacity = 0.45
            });
            return true;
        }

        return false;
    }

    private void FlushParagraph(List<string> paragraph)
    {
        if (paragraph.Count == 0)
        {
            return;
        }

        AddInlineTextBlock(string.Join(Environment.NewLine, paragraph));
        paragraph.Clear();
    }

    private void AddBullet(string text)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        panel.Children.Add(new TextBlock { Text = "•", FontSize = 14, VerticalAlignment = VerticalAlignment.Top });
        panel.Children.Add(CreateInlineTextBlock(text));
        _content.Children.Add(panel);
    }

    private void AddQuote(string text)
    {
        _content.Children.Add(new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = Brushes.Gray,
            Padding = new Thickness(10, 2, 0, 2),
            Opacity = 0.85,
            Child = CreateInlineTextBlock(text)
        });
    }

    private void AddCodeBlock(IReadOnlyList<string> lines)
    {
        _content.Children.Add(new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Black,
            Opacity = 0.82,
            Child = new TextBlock
            {
                Text = string.Join(Environment.NewLine, lines),
                FontFamily = FontFamily.Parse("Consolas"),
                TextWrapping = TextWrapping.Wrap
            }
        });
    }

    private void AddTextBlock(string text, FontWeight weight, double fontSize)
    {
        _content.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = weight,
            FontSize = fontSize,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void AddInlineTextBlock(string text)
    {
        _content.Children.Add(CreateInlineTextBlock(text));
    }

    private static TextBlock CreateInlineTextBlock(string text)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap };
        foreach (var inline in ParseInline(text))
        {
            block.Inlines!.Add(inline);
        }

        return block;
    }

    private static IEnumerable<Inline> ParseInline(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var code = text.IndexOf('`', index);
            var bold = text.IndexOf("**", index, StringComparison.Ordinal);
            var next = MinPositive(code, bold);
            if (next < 0)
            {
                yield return new Run(text[index..]);
                yield break;
            }

            if (next > index)
            {
                yield return new Run(text[index..next]);
            }

            if (next == code)
            {
                var end = text.IndexOf('`', code + 1);
                if (end < 0)
                {
                    yield return new Run(text[code..]);
                    yield break;
                }

                yield return new Run(text[(code + 1)..end])
                {
                    FontFamily = FontFamily.Parse("Consolas")
                };
                index = end + 1;
            }
            else
            {
                var end = text.IndexOf("**", bold + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    yield return new Run(text[bold..]);
                    yield break;
                }

                yield return new Run(text[(bold + 2)..end]) { FontWeight = FontWeight.Bold };
                index = end + 2;
            }
        }
    }

    private static int MinPositive(int left, int right)
    {
        return (left, right) switch
        {
            (< 0, < 0) => -1,
            (< 0, _) => right,
            (_, < 0) => left,
            _ => Math.Min(left, right)
        };
    }

    private static int CountHeadingLevel(string line)
    {
        var count = 0;
        while (count < line.Length && count < 6 && line[count] == '#')
        {
            count++;
        }

        return count > 0 && count < line.Length && char.IsWhiteSpace(line[count]) ? count : 0;
    }

    private static double HeadingSize(int level)
    {
        return level switch
        {
            1 => 24,
            2 => 20,
            3 => 17,
            _ => 15
        };
    }

    private static string CleanText(string value)
    {
        return WebUtility.HtmlDecode(HtmlTagRegex.Replace(value, string.Empty)).Trim();
    }
}
