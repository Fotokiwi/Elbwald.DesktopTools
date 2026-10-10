namespace Elbwald.DesktopTools.Contracts.Media.MetadataExporting;

public sealed record MetadataExportInspection(
    bool SidecarExists,
    bool SidecarElbwaldManaged,
    bool EmbeddedXmpExists,
    bool EmbeddedXmpElbwaldManaged)
{
    public string Summary =>
        EmbeddedXmpElbwaldManaged ? "Elbwald-XMP im JPEG vorhanden"
        : SidecarElbwaldManaged ? "Elbwald-XMP-Sidecar vorhanden"
        : SidecarExists || EmbeddedXmpExists ? "Fremde XMP vorhanden"
        : "Nur Elbwald DB";
}
