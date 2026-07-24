using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Snippets.App.Views;

public sealed class MarkdownSourceEditor : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MarkdownSourceEditor, string>(
            nameof(Text),
            string.Empty,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private static readonly FontFamily MonoFont = FontFamily.Parse("Consolas");
    private readonly TextBox _textBox;

    private bool _syncingText;

    public MarkdownSourceEditor()
    {
        _textBox = new TextBox
        {
            AcceptsReturn = true,
            FontFamily = MonoFont,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gainsboro,
            CaretBrush = Brushes.White,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(110, 74, 144, 226)),
            SelectionForegroundBrush = Brushes.White,
            ClearSelectionOnLostFocus = false,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2)
        };
        _textBox.TextChanged += (_, _) =>
        {
            if (_syncingText)
            {
                return;
            }

            Text = _textBox.Text ?? string.Empty;
        };
        Content = _textBox;
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool WrapSelectionWithCopyTag()
    {
        var text = _textBox.Text ?? string.Empty;
        var start = Math.Min(_textBox.SelectionStart, _textBox.SelectionEnd);
        var end = Math.Max(_textBox.SelectionStart, _textBox.SelectionEnd);
        if (start == end || start < 0 || end > text.Length)
        {
            return false;
        }

        var selected = text[start..end];
        var label = CreateLabel(selected);
        var id = CreateId(label);
        var wrapped = $"<span data-copy-id=\"{id}\" data-copy-label=\"{EscapeAttribute(label)}\">{selected}</span>";
        var updated = text[..start] + wrapped + text[end..];

        _syncingText = true;
        _textBox.Text = updated;
        _syncingText = false;
        Text = updated;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var length = _textBox.Text?.Length ?? 0;
            var selectionStart = Math.Clamp(start, 0, length);
            var selectionEnd = Math.Clamp(start + wrapped.Length, 0, length);
            _textBox.Focus();
            _textBox.SelectionStart = selectionStart;
            _textBox.SelectionEnd = selectionEnd;
        }, DispatcherPriority.Background);
        return true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            var value = change.NewValue as string ?? string.Empty;
            if (_textBox.Text != value)
            {
                _syncingText = true;
                _textBox.Text = value;
                _syncingText = false;
            }
        }
    }

    private static string CreateLabel(string selected)
    {
        var line = selected
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(value => value.Trim())
            .FirstOrDefault(value => value.Length > 0) ?? "Snippet";
        return line.Length <= 32 ? line : line[..32].TrimEnd();
    }

    private static string CreateId(string label)
    {
        var normalized = Regex.Replace(label.ToLowerInvariant(), @"[^a-z0-9]+", ".", RegexOptions.CultureInvariant).Trim('.');
        return string.IsNullOrWhiteSpace(normalized)
            ? "snippet"
            : normalized;
    }

    private static string EscapeAttribute(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
