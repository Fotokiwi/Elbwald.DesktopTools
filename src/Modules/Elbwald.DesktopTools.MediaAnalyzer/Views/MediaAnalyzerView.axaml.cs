using Avalonia.Controls;
using Elbwald.DesktopTools.MediaAnalyzer.ViewModels;

namespace Elbwald.DesktopTools.MediaAnalyzer.Views;

public partial class MediaAnalyzerView
    : UserControl
{
    public MediaAnalyzerView()
    {
        InitializeComponent();

        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is MediaAnalyzerViewModel viewModel)
            {
                await viewModel.RestoreLastAnalysisAsync();
            }
        };
    }
}
