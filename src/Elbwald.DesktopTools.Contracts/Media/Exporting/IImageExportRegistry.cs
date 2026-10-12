namespace Elbwald.DesktopTools.Contracts.Media.Exporting;

public interface IImageExportRegistry
{
    event EventHandler? ProvidersChanged;

    IReadOnlyList<IImageExportProvider> Providers { get; }

    IImageExportProvider? FindByFormatId(string formatId);
}
