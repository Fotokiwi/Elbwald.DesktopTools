using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Views;

public partial class AudioExportWindow : Window
{
    public AudioExportWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
