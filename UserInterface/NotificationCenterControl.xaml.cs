using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Windows.Foundation;
using Windows.UI;

namespace EricGameLauncher;

public enum NotificationLevel { Info, Success, Error }

public sealed class NotificationMessage : INotifyPropertyChanged
{
    public required string FullText { get; init; }
    public required string Glyph { get; init; }
    public required Brush Accent { get; init; }
    public required NotificationLevel Level { get; init; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(TextWrapping));
            OnPropertyChanged(nameof(TextTrimming));
            OnPropertyChanged(nameof(ExpandGlyph));
        }
    }

    public TextWrapping TextWrapping => IsExpanded ? TextWrapping.Wrap : TextWrapping.NoWrap;
    public TextTrimming TextTrimming => IsExpanded ? TextTrimming.None : TextTrimming.CharacterEllipsis;
    public string ExpandGlyph => IsExpanded ? "\uE70D" : "\uE70E";
    public bool CanExpand { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed partial class NotificationCenterControl : UserControl
{
    private const int MaxVisible = 10;
    private const int VisibleSeconds = 5;
    private const int ExpandMilliseconds = 200;
    private static readonly Color ErrorColor = Color.FromArgb(255, 196, 43, 28);
    private static readonly Color SuccessColor = Color.FromArgb(255, 15, 123, 15);
    private static readonly Color InfoColor = Color.FromArgb(255, 0, 95, 184);

    private readonly ObservableCollection<NotificationMessage> _messages = new();
    private readonly Dictionary<NotificationMessage, DispatcherTimer> _timers = new();
    private readonly Dictionary<NotificationMessage, HeightAnimation> _heightAnimations = new();
    private readonly HashSet<NotificationMessage> _removing = new();

    private sealed class HeightAnimation
    {
        public required Storyboard Storyboard { get; init; }
        public required Border Card { get; init; }
        public required Grid Content { get; init; }
        public required RectangleGeometry Clip { get; init; }
        public required SizeChangedEventHandler Handler { get; init; }
        public required RoutedEventHandler UnloadedHandler { get; init; }
        public required double TargetHeight { get; init; }
    }

    public NotificationCenterControl()
    {
        InitializeComponent();
        ToastHost.ItemsSource = _messages;
    }

    public void Notify(string? message, NotificationLevel level = NotificationLevel.Error)
    {
        string text = (message ?? "").Trim();
        if (text.Length == 0) return;
        while (_messages.Count >= MaxVisible) RemoveImmediate(_messages[0]);
        var item = new NotificationMessage
        {
            FullText = text,
            Glyph = level switch
            {
                NotificationLevel.Success => "\uE73E",
                NotificationLevel.Info => "\uE946",
                _ => "\uE783"
            },
            Accent = new SolidColorBrush(level switch
            {
                NotificationLevel.Success => SuccessColor,
                NotificationLevel.Info => InfoColor,
                _ => ErrorColor
            }),
            Level = level
        };
        _messages.Add(item);
        ArmTimer(item);
        LogService.Write("UI", $"Notification level={level} message={text}");
    }

    public void Clear()
    {
        foreach (var item in _messages.ToArray()) RemoveImmediate(item);
    }

    private void ArmTimer(NotificationMessage item)
    {
        StopTimer(item);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(VisibleSeconds) };
        timer.Tick += (_, _) =>
        {
            if (!item.IsExpanded) RemoveAnimated(item);
        };
        _timers[item] = timer;
        timer.Start();
    }

    private void StopTimer(NotificationMessage item)
    {
        if (_timers.Remove(item, out var timer)) timer.Stop();
    }

    private void RemoveImmediate(NotificationMessage item)
    {
        StopTimer(item);
        StopHeightAnimation(item);
        _removing.Remove(item);
        _messages.Remove(item);
    }

    private void RemoveAnimated(NotificationMessage item)
    {
        if (!_removing.Add(item)) return;
        StopTimer(item);
        StopHeightAnimation(item);
        if (ToastHost.ContainerFromItem(item) is not FrameworkElement container)
        {
            RemoveImmediate(item);
            return;
        }
        try
        {
            var transform = container.RenderTransform as TranslateTransform ?? new TranslateTransform();
            container.RenderTransform = transform;
            container.Opacity = 1;
            var storyboard = new Storyboard();
            var fade = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150))
            };
            Storyboard.SetTarget(fade, container);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);
            var slide = new DoubleAnimation
            {
                From = transform.X,
                To = 28,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(slide, transform);
            Storyboard.SetTargetProperty(slide, "X");
            storyboard.Children.Add(slide);
            storyboard.Completed += (_, _) => RemoveImmediate(item);
            storyboard.Begin();
        }
        catch (Exception ex)
        {
            LogService.Write("UI", "Notification remove animation failed", ex);
            RemoveImmediate(item);
        }
    }

    private static ContentPresenter? FindContainer(ItemsControl host, object item) => host.ContainerFromItem(item) as ContentPresenter;

    private static Border? FindCard(ContentPresenter? container)
    {
        if (container == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(container); i++)
            if (VisualTreeHelper.GetChild(container, i) is Border border) return border;
        return null;
    }

    private static T? FindColumnChild<T>(Border card, int column) where T : FrameworkElement
    {
        if (card.Child is not Grid grid) return null;
        foreach (var child in grid.Children)
            if (child is T typed && Grid.GetColumn(typed) == column) return typed;
        return null;
    }

    private void NotificationCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border card) return;
        var message = FindColumnChild<TextBlock>(card, 1);
        var expand = FindColumnChild<Button>(card, 2);
        if (message != null && expand != null && card.DataContext is NotificationMessage item)
        {
            card.UpdateLayout();
            if (message.IsTextTrimmed) item.CanExpand = true;
            void ApplyExpandVisibility() => expand.Visibility = item.CanExpand ? Visibility.Visible : Visibility.Collapsed;
            message.IsTextTrimmedChanged += (_, _) =>
            {
                if (message.IsTextTrimmed && !item.CanExpand)
                {
                    item.CanExpand = true;
                    LogService.Write("UI", "Notification becomes expandable after layout");
                }
                ApplyExpandVisibility();
            };
            ApplyExpandVisibility();
            DispatcherQueue.TryEnqueue(ApplyExpandVisibility);
            LogService.Write("UI", $"Notification canExpand={item.CanExpand}");
        }
        var transform = new TranslateTransform { X = 28 };
        card.RenderTransform = transform;
        card.Opacity = 0;
        var storyboard = new Storyboard();
        var slide = new DoubleAnimation
        {
            From = 28,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, "X");
        storyboard.Children.Add(slide);
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(150)) };
        Storyboard.SetTarget(fade, card);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);
        storyboard.Begin();
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not NotificationMessage item) return;
        bool target = !item.IsExpanded;
        var card = FindCard(FindContainer(ToastHost, item));
        LogService.Write("UI", $"Notification toggle requested expanded={target} canExpand={item.CanExpand} actual={card?.ActualHeight ?? 0:0.0}");
        if (card == null)
        {
            FinishToggle(item, target);
            return;
        }
        AnimateExpansion(card, item, target);
    }

    private void AnimateExpansion(Border card, NotificationMessage item, bool target)
    {
        double from = card.ActualHeight;
        StopHeightAnimation(item);
        if (from <= 0)
        {
            FinishToggle(item, target);
            return;
        }

        double to;
        try
        {
            item.IsExpanded = target;
            card.ClearValue(FrameworkElement.HeightProperty);
            card.UpdateLayout();
            to = card.ActualHeight;
        }
        catch (Exception ex)
        {
            LogService.Write("UI", "Notification expand measure failed", ex);
            card.ClearValue(FrameworkElement.HeightProperty);
            FinishToggle(item, target);
            return;
        }

        FinishToggle(item, target);
        if (to <= 0 || Math.Abs(to - from) < 0.5)
        {
            LogService.Write("UI", $"Notification height transition skipped expanded={target} from={from:0.0} to={to:0.0}");
            return;
        }

        LogService.Write("UI", $"Notification height transition started expanded={target} from={from:0.0} to={to:0.0}");
        if (card.Child is not Grid content)
        {
            LogService.Write("UI", "Notification height transition skipped because card content is unavailable");
            return;
        }

        card.Height = from;
        var clip = new RectangleGeometry { Rect = new Rect(0, 0, content.ActualWidth, content.ActualHeight) };
        content.Clip = clip;
        SizeChangedEventHandler handler = (_, args) => clip.Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height);
        content.SizeChanged += handler;
        var storyboard = new Storyboard();
        var height = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(ExpandMilliseconds)),
            EnableDependentAnimation = true,
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(height, card);
        Storyboard.SetTargetProperty(height, "Height");
        storyboard.Children.Add(height);
        HeightAnimation entry = null!;
        RoutedEventHandler unloadedHandler = (_, _) =>
        {
            EndHeightAnimation(item, entry);
        };
        entry = new HeightAnimation
        {
            Storyboard = storyboard,
            Card = card,
            Content = content,
            Clip = clip,
            Handler = handler,
            UnloadedHandler = unloadedHandler,
            TargetHeight = to
        };
        _heightAnimations[item] = entry;
        card.Unloaded += unloadedHandler;
        storyboard.Completed += (_, _) =>
        {
            EndHeightAnimation(item, entry);
        };
        storyboard.Begin();
    }

    private void FinishToggle(NotificationMessage item, bool expanded)
    {
        item.IsExpanded = expanded;
        if (expanded) StopTimer(item); else ArmTimer(item);
        LogService.Write("UI", $"Notification toggle finished expanded={expanded}");
    }

    private void StopHeightAnimation(NotificationMessage item)
    {
        if (!_heightAnimations.TryGetValue(item, out var entry)) return;
        try { entry.Storyboard.Stop(); } catch { }
        CleanupHeightAnimation(item, entry);
    }

    private void CleanupHeightAnimation(NotificationMessage item, HeightAnimation entry)
    {
        if (_heightAnimations.TryGetValue(item, out var current) && ReferenceEquals(current, entry))
            _heightAnimations.Remove(item);
        try { entry.Storyboard.Stop(); } catch { }
        try { entry.Card.Unloaded -= entry.UnloadedHandler; } catch { }
        try { entry.Content.SizeChanged -= entry.Handler; } catch { }
        entry.Content.Clip = null;
        entry.Card.ClearValue(FrameworkElement.HeightProperty);
    }

    private void EndHeightAnimation(NotificationMessage item, HeightAnimation entry)
    {
        if (!_heightAnimations.TryGetValue(item, out var current) || !ReferenceEquals(current, entry)) return;
        entry.Card.Height = entry.TargetHeight;
        CleanupHeightAnimation(item, entry);
        LogService.Write("UI", $"Notification height animation ended expanded={item.IsExpanded} actual={entry.Card.ActualHeight:0.0}");
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is NotificationMessage item) RemoveAnimated(item);
    }
}
