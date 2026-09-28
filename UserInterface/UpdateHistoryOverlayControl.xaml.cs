using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace EricGameLauncher;

public sealed partial class UpdateHistoryOverlayControl : UserControl
{
    private const string SpyScript = """
        <script>
        (function () {
            function notify() {
                try {
                    var nodes = document.querySelectorAll('.history-entry');
                    if (nodes.length === 0) { return; }
                    var index = 0;
                    var atBottom = (window.innerHeight + window.scrollY) >= (document.body.scrollHeight - 4);
                    if (atBottom) {
                        index = nodes.length - 1;
                    } else {
                        for (var i = 0; i < nodes.length; i++) {
                            if (nodes[i].getBoundingClientRect().top <= 96) { index = i; } else { break; }
                        }
                    }
                    window.chrome.webview.postMessage('select:' + index);
                } catch (err) { }
            }
            window.addEventListener('load', function () {
                window.scrollTo(0, document.body.scrollHeight);
                setTimeout(notify, 80);
            });
            var handle = null;
            window.addEventListener('scroll', function () {
                if (handle) { clearTimeout(handle); }
                handle = setTimeout(notify, 80);
            }, { passive: true });
        })();
        </script>
        """;

    private WebView2? _webView;
    private bool _suppressScroll;

    public UpdateHistoryOverlayControl()
    {
        InitializeComponent();
    }

    public void ApplyLocalization()
    {
        HistoryTitleText.Text = Text.T("Update_HistoryTitle");
        ToolTipService.SetToolTip(HistoryCloseButton, Text.T("Close"));
    }

    public async Task ShowAsync()
    {
        using (LogService.StartOperation("Update", "UpdateHistoryOverlayShow"))
        {
            ApplyLocalization();
            Visibility = Visibility.Visible;
            ResetContent();

            try
            {
                var entries = await UpdateService.GetReleaseHistoryAsync();
                if (entries == null)
                {
                    LogService.Write("Update", "History overlay load failed: releases unavailable");
                    ShowMessage(Text.T("Update_HistoryFailed"));
                    return;
                }

                LogService.Write("Update", $"History overlay loaded entries={entries.Count}");
                BuildList(entries);
                await RenderAsync(entries);
            }
            catch (Exception ex)
            {
                LogService.Write("Update", "History overlay failed", ex);
                ShowMessage(Text.T("Update_HistoryFailed"));
            }
        }
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        foreach (var child in HistoryViewHost.Children)
        {
            if (child is WebView2 webView)
            {
                try { webView.Close(); } catch (Exception ex) { LogService.Write("Update", "History overlay webview close failed", ex); }
            }
        }
        HistoryViewHost.Children.Clear();
        _webView = null;
        LogService.Write("Update", "History overlay hidden");
    }

    private void ResetContent()
    {
        _webView = null;
        _suppressScroll = true;
        try { HistoryList.Items.Clear(); }
        finally { _suppressScroll = false; }

        HistoryViewHost.Children.Clear();
        HistoryMessageText.Visibility = Visibility.Collapsed;
        HistoryLoadingRing.Visibility = Visibility.Visible;
    }

    private void ShowMessage(string message)
    {
        HistoryLoadingRing.Visibility = Visibility.Collapsed;
        HistoryViewHost.Children.Clear();
        HistoryMessageText.Text = message;
        HistoryMessageText.Visibility = Visibility.Visible;
    }

    private void BuildList(List<UpdateService.ReleaseHistoryEntry> entries)
    {
        _suppressScroll = true;
        try
        {
            HistoryList.Items.Clear();
            foreach (var entry in entries)
            {
                HistoryList.Items.Add(BuildListItem(entry));
            }
            HistoryList.SelectedIndex = entries.Count > 0 ? entries.Count - 1 : -1;
        }
        finally { _suppressScroll = false; }
    }

    private static ListViewItem BuildListItem(UpdateService.ReleaseHistoryEntry entry)
    {
        string title = string.IsNullOrEmpty(entry.Name) ? entry.TagName : entry.Name;

        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (entry.PublishedAt != null)
        {
            meta.Children.Add(new TextBlock
            {
                Text = entry.PublishedAt.Value.ToLocalTime().ToString("yyyy-MM-dd"),
                FontSize = 11,
                Opacity = 0.65
            });
        }
        panel.Children.Add(meta);

        return new ListViewItem { Content = panel, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }

    private async Task RenderAsync(List<UpdateService.ReleaseHistoryEntry> entries)
    {
        if (entries.Count == 0)
        {
            ShowMessage(Text.T("Update_HistoryEmpty"));
            return;
        }

        var (html, plainText) = BuildMarkup(entries);
        var (view, webView) = await HtmlViewBuilder.CreateAsync(this, html, plainText, 0, 0, SpyScript);
        _webView = webView;

        if (webView?.CoreWebView2 != null)
        {
            webView.CoreWebView2.WebMessageReceived += (s, e) =>
            {
                try
                {
                    string message = e.TryGetWebMessageAsString();
                    if (string.IsNullOrEmpty(message) || !message.StartsWith("select:")) return;
                    if (!int.TryParse(message.Substring("select:".Length), out int index)) return;
                    SelectFromScroll(index);
                }
                catch (Exception ex) { LogService.Write("Update", "History scroll sync failed", ex); }
            };
        }

        HistoryLoadingRing.Visibility = Visibility.Collapsed;
        HistoryMessageText.Visibility = Visibility.Collapsed;
        HistoryViewHost.Children.Clear();
        HistoryViewHost.Children.Add(view);
    }

    private void SelectFromScroll(int index)
    {
        if (index < 0 || index >= HistoryList.Items.Count) return;
        if (HistoryList.SelectedIndex == index) return;

        _suppressScroll = true;
        try
        {
            HistoryList.SelectedIndex = index;
            HistoryList.ScrollIntoView(HistoryList.Items[index]);
        }
        finally { _suppressScroll = false; }
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressScroll) return;
        int index = HistoryList.SelectedIndex;
        if (index < 0 || _webView?.CoreWebView2 == null) return;

        LogService.Write("Update", $"History jump to index={index}");
        _ = _webView.CoreWebView2.ExecuteScriptAsync($"document.getElementById('history-entry-{index}').scrollIntoView({{block:'start'}});");
    }

    private void HistoryClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private static (string Html, string Plain) BuildMarkup(List<UpdateService.ReleaseHistoryEntry> entries)
    {
        var html = new StringBuilder();
        var plain = new StringBuilder();

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            string title = string.IsNullOrEmpty(entry.Name) ? entry.TagName : entry.Name;
            string date = entry.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";

            html.Append($"<div class='history-entry' id='history-entry-{i}'><div class='history-head'>");
            html.Append($"<span class='history-ver'>{System.Net.WebUtility.HtmlEncode(title)}</span>");
            if (!string.IsNullOrEmpty(date)) html.Append($"<span class='history-date'>{date}</span>");
            html.Append("</div>");

            string entryHtml = string.IsNullOrEmpty(entry.BodyHtml) ? MarkdownHtml.ToHtml(entry.Body) : entry.BodyHtml;
            html.Append(entryHtml).Append("</div>");

            plain.AppendLine($"## {title} {date}".TrimEnd());
            plain.AppendLine();
            plain.AppendLine(entry.Body);
            plain.AppendLine();
        }

        return (html.ToString(), plain.ToString());
    }
}
