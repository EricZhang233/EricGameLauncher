using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;

namespace EricGameLauncher;

public sealed class LauncherAdaptiveGridView : GridView
{
    public static readonly DependencyProperty DesiredWidthProperty = DependencyProperty.Register(nameof(DesiredWidth), typeof(double), typeof(LauncherAdaptiveGridView), new PropertyMetadata(134.0, (owner, _) => ((LauncherAdaptiveGridView)owner).RecalculateAdaptiveWidth()));
    public double DesiredWidth { get => (double)GetValue(DesiredWidthProperty); set => SetValue(DesiredWidthProperty, value); }
    private ScrollViewer? _scrollViewer;
    private Thickness? _basePadding;
    private double _calculatedItemWidth = double.NaN;
    public event Action<AppItem>? ActivationRequested;

    protected override DependencyObject GetContainerForItemOverride() => new LauncherGridViewItem(item => ActivationRequested?.Invoke(item));

    public LauncherAdaptiveGridView()
    {
        IsTabStop = false;
        UseLayoutRounding = false;
        Items.VectorChanged += (_, _) => RecalculateAdaptiveWidth();
        SizeChanged += (_, _) => RecalculateAdaptiveWidth();
        Loaded += (_, _) => AttachScrollViewer();
        Unloaded += (_, _) => DetachScrollViewer();
    }

    public void RecalculateAdaptiveWidth()
    {
        if (ActualWidth <= 0 || ItemsPanelRoot is not ItemsWrapGrid itemsPanel)
            return;

        if (_basePadding is null)
        {
            if (!IsLoaded) return;
            _basePadding = Padding;
        }

        double scrollbarWidth = GetReservedVerticalScrollbarWidth();
        var basePadding = _basePadding.Value;
        var targetPadding = new Thickness(
            basePadding.Left + scrollbarWidth,
            basePadding.Top,
            basePadding.Right,
            basePadding.Bottom);

        if (Math.Abs(Padding.Left - targetPadding.Left) > 0.01)
            Padding = targetPadding;

        itemsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;

        double viewportWidth = _scrollViewer is { ViewportWidth: > 0 }
            ? Math.Min(ActualWidth, _scrollViewer.ViewportWidth)
            : ActualWidth;
        double leftInset = Padding.Left + BorderThickness.Left + itemsPanel.Margin.Left;
        double rightInset = Padding.Right + BorderThickness.Right + itemsPanel.Margin.Right;
        double availableWidth = viewportWidth - leftInset - rightInset;

        double columns = Math.Max(1, Math.Round(availableWidth / DesiredWidth));
        var margin = ContainerFromIndex(0) is FrameworkElement first ? first.Margin : default;
        double calculatedWidth = availableWidth / columns - margin.Left - margin.Right;
        if (!double.IsFinite(calculatedWidth) || calculatedWidth <= 0)
            return;

        if (Math.Abs(_calculatedItemWidth - calculatedWidth) <= 0.01)
            return;

        _calculatedItemWidth = calculatedWidth;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(itemsPanel); i++)
        {
            if (VisualTreeHelper.GetChild(itemsPanel, i) is FrameworkElement container
                && (!double.IsFinite(container.Width) || Math.Abs(container.Width - calculatedWidth) > 0.01))
            {
                container.Width = calculatedWidth;
            }
        }
    }

    private double GetReservedVerticalScrollbarWidth()
    {
        if (_scrollViewer is not { ViewportWidth: > 0 } scrollViewer)
            return 0;

        return Math.Max(0, scrollViewer.ActualWidth - scrollViewer.ViewportWidth);
    }

    private void AttachScrollViewer()
    {
        DetachScrollViewer();
        _scrollViewer = FindScrollViewer(this);
        if (_scrollViewer != null)
        {
            _scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
            _scrollViewer.SizeChanged += ScrollViewer_SizeChanged;
        }

        RecalculateAdaptiveWidth();
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer != null)
        {
            _scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
            _scrollViewer.SizeChanged -= ScrollViewer_SizeChanged;
        }
        _scrollViewer = null;
    }

    private void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        => RecalculateAdaptiveWidth();

    private void ScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        => RecalculateAdaptiveWidth();

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scrollViewer)
                return scrollViewer;

            var descendant = FindScrollViewer(child);
            if (descendant != null)
                return descendant;
        }

        return null;
    }

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is ContentControl content)
        {
            content.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            content.VerticalContentAlignment = VerticalAlignment.Stretch;
        }
        if (element is FrameworkElement container && double.IsFinite(_calculatedItemWidth))
            container.Width = _calculatedItemWidth;
    }

}
