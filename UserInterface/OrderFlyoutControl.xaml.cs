using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;

namespace EricGameLauncher;

public sealed partial class OrderFlyoutControl : UserControl
{
    private readonly ObservableCollection<AppItem> _tempOrder = new();

    public event Action<IReadOnlyList<AppItem>>? OrderCommitted;
    public Flyout Flyout => OrderFlyout;

    public OrderFlyoutControl()
    {
        InitializeComponent();
        OrderItemsControl.ItemsSource = _tempOrder;
    }

    public void Open(IEnumerable<AppItem> items)
    {
        try
        {
            _tempOrder.Clear();
            foreach (var item in items) _tempOrder.Add(item);
        }
        catch (Exception ex) { LogService.Write("App", "OrderFlyoutControl Open failed", ex); }
    }

    public void ApplyLocalization()
    {
        SortTitle.Text = Text.T("Sort_Title");
        SortDescription.Text = Text.T("Sort_Description");
    }

    private void OrderFlyout_Opening(object sender, object e)
    {
        if (_tempOrder.Count == 0) LogService.Write("App", "OrderFlyoutControl opened with empty list");
    }

    private void OrderFlyout_Closed(object sender, object e)
    {
        try
        {
            if (_tempOrder.Count == 0) return;
            var newOrder = _tempOrder.ToList();
            ItemService.SaveOrder(newOrder);
            OrderCommitted?.Invoke(newOrder);
            LogService.Write("App", $"OrderFlyoutControl committed order count={newOrder.Count}");
        }
        catch (Exception ex) { LogService.Write("App", "OrderFlyoutControl commit failed", ex); }
    }

    private void OrderList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        try
        {
            if (args.InRecycleQueue) return;
            if (args.ItemContainer is ListViewItem lvi)
            {
                var sortButtons = FindChildByName(lvi, "SortButtons") as StackPanel;
                if (sortButtons != null && sortButtons.Children.Count >= 2)
                {
                    if (sortButtons.Children[0] is Button moveUpBtn)
                        ToolTipService.SetToolTip(moveUpBtn, Text.T("Sort_MoveUp"));
                    if (sortButtons.Children[1] is Button moveDownBtn)
                        ToolTipService.SetToolTip(moveDownBtn, Text.T("Sort_MoveDown"));
                }
            }
        }
        catch (Exception ex) { LogService.Write("App", "OrderFlyoutControl container content failed", ex); }
    }

    private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            MoveItem((sender as Button)?.Tag as AppItem, -1);
        }
        catch (Exception ex) { LogService.Write("App", "OrderFlyoutControl move up failed", ex); }
    }

    private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            MoveItem((sender as Button)?.Tag as AppItem, 1);
        }
        catch (Exception ex) { LogService.Write("App", "OrderFlyoutControl move down failed", ex); }
    }

    private void MoveItem(AppItem? item, int offset)
    {
        if (item == null || _tempOrder.Count == 0) return;

        var list = _tempOrder.ToList();
        if (offset < 0)
            ItemService.MoveUp(list, item.Id);
        else
            ItemService.MoveDown(list, item.Id);

        _tempOrder.Clear();
        foreach (var entry in list) _tempOrder.Add(entry);

        OrderItemsControl.SelectedItem = item;
        OrderItemsControl.ScrollIntoView(item);
    }

    private void OrderList_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (sender is ListView listView && listView.SelectedItem is AppItem item)
        {
            if (e.Key == Windows.System.VirtualKey.W || e.Key == Windows.System.VirtualKey.Up)
            {
                MoveItem(item, -1);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.S || e.Key == Windows.System.VirtualKey.Down)
            {
                MoveItem(item, 1);
                e.Handled = true;
            }
        }
    }

    private void OrderItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && FindChildByName(grid, "SortButtons") is StackPanel panel)
            panel.Opacity = 1;
    }

    private void OrderItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && FindChildByName(grid, "SortButtons") is StackPanel panel)
            panel.Opacity = 0;
    }

    private static DependencyObject? FindChildByName(DependencyObject parent, string name)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && element.Name == name) return child;
            var result = FindChildByName(child, name);
            if (result != null) return result;
        }
        return null;
    }
}
