using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Elbwald.DesktopTools.App.Views;

public partial class DiagnosticsLogView
    : UserControl
{
    public DiagnosticsLogView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
