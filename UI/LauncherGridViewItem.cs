using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace EricGameLauncher;

internal sealed class LauncherGridViewItem(Action<AppItem> activate) : GridViewItem
{
    protected override AutomationPeer OnCreateAutomationPeer() => new LauncherItemPeer(this, activate);

    private sealed class LauncherItemPeer(LauncherGridViewItem owner, Action<AppItem> activate) : GridViewItemAutomationPeer(owner), IInvokeProvider
    {
        protected override object GetPatternCore(PatternInterface patternInterface) => patternInterface == PatternInterface.Invoke ? this : base.GetPatternCore(patternInterface);

        void IInvokeProvider.Invoke()
        {
            owner.DispatcherQueue.TryEnqueue(() => { if (owner.IsEnabled && owner.Content is AppItem item) activate(item); });
        }
    }
}
