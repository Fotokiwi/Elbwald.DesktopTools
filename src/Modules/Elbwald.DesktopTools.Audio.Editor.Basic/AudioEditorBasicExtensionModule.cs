using Elbwald.DesktopTools.Audio.Editor.Basic.Playback;
using Elbwald.DesktopTools.Audio.Editor.Basic.Services;
using Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;
using Elbwald.DesktopTools.Audio.Editor.Basic.Views;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Contracts.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Audio.Editor.Basic;

/// <summary>
/// First-party audio editing workspace. The native playback backend is isolated
/// behind the playback service; project mixing and DSP remain Elbwald-owned.
/// </summary>
public sealed class AudioEditorBasicExtensionModule : IToolModule
{
    public const string ExtensionId = "elbwald.audio.editor.basic";

    public string Id => ExtensionId;

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<AudioStudioPickerStateStore>();
        services.AddSingleton<IAudioStudioStoragePicker, AvaloniaAudioStudioStoragePicker>();
        services.AddSingleton<WavMetadataReader>();
        services.AddSingleton<WavSampleDecoder>();
        services.AddSingleton<WaveformCache>();
        services.AddSingleton<RestorationAnalysisService>();
        services.AddSingleton<StemRepairAnalysisService>();
        services.AddSingleton<StemRepairAuditionService>();
        services.AddSingleton<AudioProjectSerializer>();
        services.AddSingleton<MasteringAnalysisService>();
        services.AddSingleton<AudioPlaybackService>();
        services.AddSingleton<EqAnalysisWindowPlacementStore>();
        services.AddSingleton<EqAnalysisWindowService>();
        services.AddSingleton<StemRepairWindowPlacementStore>();
        services.AddSingleton<StemRepairWindowService>();
        services.AddSingleton<EffectsHelpWindowService>();
        services.AddSingleton<AudioWavExportService>();
        services.AddSingleton<AudioExportCheckService>();
        services.AddSingleton<AudioExportWindowService>();

        services.AddNavigationContribution(
            new NavigationContribution(
                ExtensionId: Id,
                Id: "audio-editor",
                RouteId: Id,
                Placement: NavigationContributionPlacement.Section,
                SectionId: "edit",
                Title: "Audiobearbeitung",
                Description: "Bis zu 16 Mono-/Stereo-Spuren verständlich mischen und bearbeiten.",
                Icon: "Music",
                Order: 20,
                ToolModuleId: Id));
    }

    public ToolModuleDescriptor GetDescriptor()
    {
        return new ToolModuleDescriptor(
            Id: Id,
            Name: "Audiobearbeitung",
            Description: "Nicht-destruktiver Mehrspur-Audioeditor mit einfacher und erweiterbarer Bedienoberfläche.",
            Icon: "Music",
            ViewModelType: typeof(AudioEditorViewModel),
            ViewType: typeof(AudioEditorView));
    }
}
