namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class RenamePlanItemViewModel
{
    public required string SourcePath { get; init; }
    public required string SourceName { get; init; }
    public required string TargetName { get; init; }
    public required string TargetPath { get; init; }
    public required string Status { get; set; }
    public required string Message { get; set; }
    public required string DateSource { get; init; }
    public required long Length { get; init; }
    public required DateTime LastWriteTimeUtc { get; init; }
    public required string Sha256 { get; init; }
    public bool IsCompanion { get; init; }
    public bool IsReady { get; set; }

    public string Kind => IsCompanion ? "Begleitdatei" : "Medium";
}
