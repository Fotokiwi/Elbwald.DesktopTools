using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Elbwald.DesktopTools.App.Views;

public partial class SettingsView
    : UserControl
{
    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
