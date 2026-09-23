using Microsoft.UI.Xaml.Controls;

namespace EricGameLauncher;

public sealed partial class MigrationOverlayControl : UserControl
{
    public MigrationOverlayControl()
    {
        InitializeComponent();
    }

    public void ApplyLocalization()
    {
        MigrationTitle.Text = Text.T("Migration_OverlayTitle");
        MigrationSubTitle.Text = Text.T("Migration_OverlaySubTitle");
    }
}
