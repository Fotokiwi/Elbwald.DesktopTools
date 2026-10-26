namespace Elbwald.DesktopTools.Audio.Editor.Basic.Models;

public sealed record MasteringAssistantPlan(
    string Level,
    string Headline,
    string Summary,
    string Step1,
    string Step2,
    string Step3,
    double GainAdjustmentDb,
    double ProposedMasterVolumeDb,
    double ProjectedTruePeakDbtp,
    double RequiredPeakReductionDb,
    bool EnableLimiter,
    bool CanAutoApply,
    string AutoApplyHint);
