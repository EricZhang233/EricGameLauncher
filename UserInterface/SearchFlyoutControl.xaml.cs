using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Diagnostics;

namespace EricGameLauncher;

public sealed partial class SearchFlyoutControl : UserControl
{
    private const double CollapsedWidth = 44;
    private const double ExpandedWidth = 280;
    private bool _isExpanded;
    private Storyboard? _transition;

    public event Action<TextBox, TextChangedEventArgs>? QueryChanged;
    public string Text => SearchBox.Text ?? string.Empty;

    public SearchFlyoutControl()
    {
        InitializeComponent();
        Unloaded += SearchControl_Unloaded;
    }

    public void ApplyLocalization()
    {
        string search = EricGameLauncher.Text.T("TitleBar_Search");
        string close = EricGameLauncher.Text.T("TitleBar_CloseSearch");
        ToolTipService.SetToolTip(SearchButton, search);
        ToolTipService.SetToolTip(SearchCloseButton, close);
        AutomationProperties.SetName(SearchButton, search);
        AutomationProperties.SetName(SearchBox, search);
        AutomationProperties.SetName(SearchCloseButton, close);
        SearchPlaceholder.Text = EricGameLauncher.Text.T("TitleBar_SearchPlaceholder");
    }

    public void FocusInput()
    {
        SetExpanded(true);
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void SetExpanded(bool expanded)
    {
        if (_isExpanded == expanded) return;
        _isExpanded = expanded;
        double fromWidth = SearchRoot.ActualWidth;
        double fromOpacity = SearchEditor.Opacity;
        _transition?.Stop();

        SearchEditor.Visibility = Visibility.Visible;
        SearchCloseButton.Visibility = Visibility.Visible;
        SearchEditor.IsHitTestVisible = expanded;
        SearchCloseButton.IsHitTestVisible = expanded;
        SearchBox.IsTabStop = expanded;
        SearchButton.IsHitTestVisible = !expanded;
        SearchButton.IsTabStop = !expanded;

        if (!expanded)
        {
            SearchButton.Focus(FocusState.Programmatic);
        }

        double targetWidth = expanded ? ExpandedWidth : CollapsedWidth;
        double targetOpacity = expanded ? 1 : 0;
        SearchRoot.Width = targetWidth;
        SearchEditor.Opacity = targetOpacity;
        SearchCloseButton.Opacity = targetOpacity;

        var storyboard = new Storyboard();
        long transitionStarted = Stopwatch.GetTimestamp();
        AddAnimation(storyboard, SearchRoot, "Width", fromWidth, targetWidth, true);
        AddAnimation(storyboard, SearchEditor, "Opacity", fromOpacity, targetOpacity);
        AddAnimation(storyboard, SearchCloseButton, "Opacity", fromOpacity, targetOpacity);
        storyboard.Completed += (_, _) =>
        {
            if (_transition != storyboard) return;
            storyboard.Stop();
            _transition = null;
            if (!_isExpanded)
            {
                SearchEditor.Visibility = Visibility.Collapsed;
                SearchCloseButton.Visibility = Visibility.Collapsed;
                SearchBox.Text = string.Empty;
            }
            LogService.Write("UI", $"Inline search transition completed expanded={_isExpanded} durationMs={Stopwatch.GetElapsedTime(transitionStarted).TotalMilliseconds:F0}");
        };
        _transition = storyboard;
        storyboard.Begin();
        LogService.Write("UI", $"Inline search expanded={expanded}");
    }

    private static void AddAnimation(Storyboard storyboard, DependencyObject target, string property,
        double from, double to, bool dependent = false)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = dependent
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => FocusInput();

    private void SearchCloseButton_Click(object sender, RoutedEventArgs e) => SetExpanded(false);

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        SetExpanded(false);
        e.Handled = true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (SearchPlaceholder != null)
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        QueryChanged?.Invoke(SearchBox, args);
    }

    private void SearchRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SearchRoot.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
    }

    private void SearchControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _transition?.Stop();
        _transition = null;
    }
}
