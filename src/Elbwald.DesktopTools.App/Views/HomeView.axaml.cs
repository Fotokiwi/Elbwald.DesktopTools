using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Elbwald.DesktopTools.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
