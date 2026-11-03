using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace EricGameLauncher;

public sealed partial class IconSizeFlyoutControl : UserControl
{
    private bool _synchronizing;
    public event Action<double>? IconSizeChanged;
    public Flyout Flyout => SizeFlyout;
    public double Value => SizeSlider.Value;

    public IconSizeFlyoutControl()
    {
        InitializeComponent();
        SizeSlider.Minimum = AppSettings.MinimumIconSize;
        SizeSlider.Maximum = AppSettings.MaximumIconSize;
    }

    public void SetValue(double value)
    {
        _synchronizing = true;
        try { SizeSlider.Value = value; }
        finally { _synchronizing = false; }
    }

    public void ApplyLocalization()
    {
        SizeTitle.Text = Text.T("Menu_IconSize");
    }

    private void BtnSizeDecrease_Click(object sender, RoutedEventArgs e)
    {
        if (SizeSlider.Value > SizeSlider.Minimum) SizeSlider.Value -= 1;
    }

    private void BtnSizeIncrease_Click(object sender, RoutedEventArgs e)
    {
        if (SizeSlider.Value < SizeSlider.Maximum) SizeSlider.Value += 1;
    }

    private void SizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_synchronizing) IconSizeChanged?.Invoke(SizeSlider.Value);
    }
}
