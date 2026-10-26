namespace Elbwald.DesktopTools.Audio.Editor.Basic.Models;

public sealed record MasteringPresetOption(
    string Key,
    string Name,
    double TargetLufs,
    double TruePeakCeilingDbtp,
    string Description,
    bool IsCustom = false)
{
    public override string ToString() => Name;
}
