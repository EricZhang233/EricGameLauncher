using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Collections.ObjectModel;

namespace EricGameLauncher;

public sealed partial class RecycleBinFlyoutControl : UserControl
{
    private readonly Flyout _flyout = new();
    private readonly ObservableCollection<AppItem> _items = new();

    public event Action? ItemsChanged;
    public Flyout Flyout => _flyout;

    public RecycleBinFlyoutControl()
    {
        InitializeComponent();
        Content = null;
        _flyout.Content = RootPanel;
        _flyout.Placement = FlyoutPlacementMode.Bottom;
        _flyout.FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter));
        _flyout.FlyoutPresenterStyle.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
        RecycleItemsControl.ItemsSource = _items;
    }

    public void SetItems(IEnumerable<AppItem> items)
    {
        _items.Clear();
        foreach (var item in items.Where(item => item.Status == (int)AppItemStatus.Recycled || item.Status == (int)AppItemStatus.PendingDeletion))
        {
            _items.Add(item);
            item.OnPropertyChanged(nameof(AppItem.TimeRemainingText));
            item.OnPropertyChanged(nameof(AppItem.TimeBadgeVisibility));
            item.OnPropertyChanged(nameof(AppItem.TitleTextDecorations));
        }
    }

    public void ApplyLocalization()
    {
        RecycleTitle.Text = Text.T("RecycleBin_Title");
        RecycleDescription.Text = Text.T("RecycleBin_Desc");
        BtnEmptyRecycleBin.Content = Text.T("RecycleBin_Empty");
    }

    private void BtnEmptyRecycleBin_Click(object sender, RoutedEventArgs e)
    {
        ItemService.MarkAllPendingDeletion();
        ItemsChanged?.Invoke();
    }

    private void RecycleMenuFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout || RecycleItemsControl.SelectedItem is not AppItem item) return;
        if (flyout.Items.Count >= 2)
        {
            if (flyout.Items[0] is MenuFlyoutItem restoreItem)
                restoreItem.Text = Text.T("RecycleBin_Restore");
            if (flyout.Items[1] is MenuFlyoutItem deleteItem)
            {
                deleteItem.Text = Text.T("RecycleBin_Delete");
                deleteItem.Visibility = item.Status == (int)AppItemStatus.PendingDeletion ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    private void MenuRestore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is AppItem item)
        {
            ItemService.RestoreItem(item.Id, null);
            ItemsChanged?.Invoke();
        }
    }

    private void MenuDeletePerm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is AppItem item)
        {
            ItemService.MarkPendingDeletion(item.Id);
            ItemsChanged?.Invoke();
        }
    }

    private void RecycleItem_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (sender is Grid grid && grid.DataContext is AppItem item)
            RecycleItemsControl.SelectedItem = item;
    }
}
