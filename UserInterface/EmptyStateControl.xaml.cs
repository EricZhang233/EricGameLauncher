using Microsoft.UI.Xaml.Controls;

namespace EricGameLauncher;

public sealed partial class EmptyStateControl : UserControl
{
    public EmptyStateControl()
    {
        InitializeComponent();
    }

    public void ApplyLocalization()
    {
        EmptyStateText.Text = Text.T("Empty_Description");
    }
}
