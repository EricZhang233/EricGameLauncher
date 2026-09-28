using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;

namespace EricGameLauncher;

internal static class HtmlViewBuilder
{
    public static async Task<(FrameworkElement View, WebView2? WebView)> CreateAsync(FrameworkElement owner, string bodyHtml, string plainFallback, double width, double height, string extraScript)
    {
        if (!System.IO.Directory.Exists(ConfigService.SystemCachePath))
            System.IO.Directory.CreateDirectory(ConfigService.SystemCachePath);
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", System.IO.Path.Combine(ConfigService.SystemCachePath, "WebView2"));

        var webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        if (width > 0) webView.Width = width;
        if (height > 0) webView.Height = height;

        try
        {
            await webView.EnsureCoreWebView2Async();
            var actualTheme = owner.ActualTheme;
            if (actualTheme == ElementTheme.Default)
                actualTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;

            string htmlContent = $@"
                <!DOCTYPE html>
                <html data-theme='{(actualTheme == ElementTheme.Dark ? "dark" : "light")}'>
                <head>
                    <meta charset='utf-8'>
                    <style>
                        :root {{ --egl-accent: {AccentHex()}; }}
                        {WebViewStyles.MarkdownCss}
                        {WebViewStyles.HistoryCss}
                        @media (prefers-color-scheme: light), (prefers-color-scheme: dark) {{
                            html[data-theme='dark'] .markdown-body {{ background-color: #0d1117; color: #e6edf3; }}
                            html[data-theme='light'] .markdown-body {{ background-color: #ffffff; color: #1F2328; }}
                        }}
                        body {{
                            box-sizing: border-box; overflow-x: hidden; margin: 0; padding: 25px;
                            background-color: transparent !important;
                        }}
                        @media (max-width: 767px) {{ body {{ padding: 15px; width: 95%; }} }}
                    </style>
                </head>
                <body class='markdown-body'>{bodyHtml}</body>
                {extraScript}
                </html>";

            webView.NavigateToString(htmlContent);
            return (webView, webView);
        }
        catch (Exception ex)
        {
            LogService.Write("Update", "HtmlViewBuilder fallback to plain text", ex);
            var fallback = new ScrollViewer
            {
                Content = new TextBlock { Text = plainFallback, TextWrapping = TextWrapping.Wrap }
            };
            if (height > 0) fallback.Height = height;
            return (fallback, null);
        }
    }

    private static string AccentHex()
    {
        try
        {
            if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var value) && value is SolidColorBrush brush)
                return $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
        }
        catch (Exception ex) { LogService.Write("UI", "AccentHex failed", ex); }
        return "#4cc2ff";
    }
}
