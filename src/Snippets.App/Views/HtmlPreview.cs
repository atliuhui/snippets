using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Snippets.App.Views;

public sealed class HtmlPreview : UserControl
{
    public static readonly StyledProperty<string> HtmlProperty =
        AvaloniaProperty.Register<HtmlPreview, string>(nameof(Html), string.Empty);

    private readonly NativeWebView _webView = new();

    public HtmlPreview()
    {
        Content = _webView;
        Width = 160;
        Height = 90;
        MinWidth = 160;
        MinHeight = 90;
        MaxWidth = 160;
        MaxHeight = 90;
        _webView.Width = 160;
        _webView.Height = 90;
        _webView.MinWidth = 160;
        _webView.MinHeight = 90;
        _webView.MaxWidth = 160;
        _webView.MaxHeight = 90;
        _webView.HorizontalAlignment = HorizontalAlignment.Stretch;
        _webView.VerticalAlignment = VerticalAlignment.Stretch;
        _webView.Source = new Uri("about:blank");
    }

    public string Html
    {
        get => GetValue(HtmlProperty);
        set => SetValue(HtmlProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HtmlProperty)
        {
            Render();
        }
    }

    private void Render()
    {
        var html = Html ?? string.Empty;
        if (string.IsNullOrWhiteSpace(html))
        {
            _webView.Source = new Uri("about:blank");
            return;
        }

        var cleanHtml = NormalizedHtml(html);
        var wrappedHtml = $"<div class=\"preview-root\">{cleanHtml}</div>";
        var doc = "<!DOCTYPE html><html><head><meta charset=\"utf-8\" /><style>" +
                  ":root{color-scheme:dark;}" +
                  "html,body{margin:0;padding:0;width:100%;height:100%;background:transparent;overflow:hidden;}" +
                  "body{background:transparent;display:block;box-sizing:border-box;overflow:hidden;}" +
                  "*{box-sizing:border-box;}" +
                  ".preview-root{display:block;transform:scale(0.7);transform-origin:left top;width:142.857%;font-size:10px;line-height:1.2;white-space:normal;overflow:hidden;}" +
                  ".preview-root *{max-width:100%;word-break:break-word;overflow:hidden;}" +
                  "img,svg,canvas,video{max-width:100%;height:auto;display:block;}" +
                  "table{border-collapse:collapse;width:100%;max-width:100%;table-layout:fixed;}" +
                  "th,td{vertical-align:top;overflow:hidden;text-overflow:ellipsis;word-break:break-word;}" +
                  "pre,code{white-space:pre-wrap;word-break:break-word;font-family:Consolas,monospace;}" +
                  "a{color:inherit;text-decoration:none;}" +
                  "html::-webkit-scrollbar, body::-webkit-scrollbar{display:none;width:0;height:0;}" +
                  "</style></head><body>" + wrappedHtml + "</body></html>";
        _webView.Source = new Uri($"data:text/html;charset=utf-8,{Uri.EscapeDataString(doc)}");
    }

    private static string NormalizedHtml(string html)
    {
        var trimmed = html.TrimStart('\uFEFF', '\0');
        trimmed = Regex.Replace(trimmed, "<!--.*?-->", string.Empty, RegexOptions.CultureInvariant | RegexOptions.Singleline);

        if (trimmed.Contains("Version:", StringComparison.OrdinalIgnoreCase) &&
            trimmed.Contains("StartHTML:", StringComparison.OrdinalIgnoreCase))
        {
            var start = FindFragmentOffset(trimmed, "StartFragment:");
            var end = FindFragmentOffset(trimmed, "EndFragment:");
            if (start >= 0 && end > start)
            {
                return trimmed.Substring(start, end - start).Trim();
            }
        }

        return trimmed.Trim();
    }

    private static int FindFragmentOffset(string text, string key)
    {
        var index = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return -1;
        }

        var start = index + key.Length;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        var end = start;
        while (end < text.Length && char.IsDigit(text[end]))
        {
            end++;
        }

        return end > start && int.TryParse(text.Substring(start, end - start), out var value)
            ? value
            : -1;
    }
}
