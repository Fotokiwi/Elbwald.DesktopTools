using Elbwald.DesktopTools.Contracts.Media.Audio;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Models;

public sealed class AudioProjectDocument
{
    public const string CurrentFormat = "ElbwaldAudioProject";
    public const int CurrentVersion = 10;

    public string Format { get; set; } = CurrentFormat;

    public int Version { get; set; } = CurrentVersion;

    public string Name { get; set; } = "Unbenannt";

    public int SampleRate { get; set; } = 48_000;

    public AudioMasterDocument Master { get; set; } = new();

    public List<AudioTrackDocument> Tracks { get; set; } = [];
}

public sealed class AudioMasterDocument
{
    public double VolumeDb { get; set; }

    public double TargetLufs { get; set; } = -12;

    public bool LimiterEnabled { get; set; } = true;

    public double LimiterCeilingDb { get; set; } = -1;

    public AudioMasterEqualizerDocument Equalizer { get; set; } = new();
}

public sealed class AudioMasterEqualizerDocument
{
    public bool EqualizerEnabled { get; set; }
    public double BassDb { get; set; }
    public double MidDb { get; set; }
    public double MidFrequencyHz { get; set; } = 1_000;
    public double TrebleDb { get; set; }
    public bool HighPassEnabled { get; set; }
    public double HighPassFrequencyHz { get; set; } = 80;
    public bool LowPassEnabled { get; set; }
    public double LowPassFrequencyHz { get; set; } = 18_000;
}

public sealed class AudioTrackDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Spur";

    public AudioChannelLayout ChannelLayout { get; set; } = AudioChannelLayout.Stereo;

    public bool IsMuted { get; set; }

    public bool IsSolo { get; set; }

    public double VolumeDb { get; set; }

    public double Pan { get; set; }

    public AudioTrackEffectsDocument Effects { get; set; } = new();

    public List<AudioClipDocument> Clips { get; set; } = [];
}

public sealed class AudioTrackEffectsDocument
{
    public bool NoiseReductionEnabled { get; set; }

    public double NoiseReductionStrength { get; set; } = 30;

    public int NoiseProfileSampleRate { get; set; }

    public int NoiseProfileFftSize { get; set; }

    public double[] NoiseProfileDb { get; set; } = [];

    /// <summary>Monitoring-only state. CloneForSave intentionally does not persist it.</summary>
    public bool NoiseReductionListenRemoved { get; set; }

    public bool ExpanderEnabled { get; set; }

    public double ExpanderThresholdDb { get; set; } = -45;

    public double ExpanderReductionDb { get; set; } = 18;

    public bool NotchEnabled { get; set; }

    public double NotchFrequencyHz { get; set; } = 6_500;

    /// <summary>0 = very narrow, 100 = broad.</summary>
    public double NotchWidthPercent { get; set; } = 30;

    public bool HumRemovalEnabled { get; set; }

    public double HumFrequencyHz { get; set; } = 50;

    public int HumHarmonics { get; set; } = 4;

    public double HumReductionDb { get; set; } = 18;

    public bool DeEsserEnabled { get; set; }

    public double DeEsserFrequencyHz { get; set; } = 6_500;

    public double DeEsserStrength { get; set; } = 35;

    /// <summary>0 = normal, 1 = mono sum, 2 = swap L/R, 3 = left to both, 4 = right to both.</summary>
    public int ChannelMode { get; set; }

    public bool PhaseInvertLeft { get; set; }

    public bool PhaseInvertRight { get; set; }

    public bool SaturationEnabled { get; set; }

    public double SaturationDriveDb { get; set; } = 4;

    public double SaturationMixPercent { get; set; } = 25;

    public bool ChorusEnabled { get; set; }

    public double ChorusRateHz { get; set; } = 0.35;

    public double ChorusDepthPercent { get; set; } = 35;

    public double ChorusMixPercent { get; set; } = 18;

    public bool StereoWidthEnabled { get; set; }

    /// <summary>0 = mono, 100 = unchanged stereo, 200 = wide.</summary>
    public double StereoWidthPercent { get; set; } = 100;

    public bool DelayEnabled { get; set; }

    public double DelayTimeMs { get; set; } = 240;

    public double DelayFeedbackPercent { get; set; } = 24;

    public double DelayMixPercent { get; set; } = 12;

    public bool DelayPingPong { get; set; }

    public bool ReverbEnabled { get; set; }

    public double ReverbMixPercent { get; set; } = 12;

    public double ReverbDecayPercent { get; set; } = 35;

    public double ReverbDampingPercent { get; set; } = 45;

    public double ReverbPreDelayMs { get; set; } = 18;

    public bool EqualizerEnabled { get; set; }

    public double BassDb { get; set; }

    public double MidDb { get; set; }

    public double TrebleDb { get; set; }

    public double MidFrequencyHz { get; set; } = 1_000;

    public bool HighPassEnabled { get; set; }

    public double HighPassFrequencyHz { get; set; } = 80;

    public bool LowPassEnabled { get; set; }

    public double LowPassFrequencyHz { get; set; } = 18_000;

    public bool CompressorEnabled { get; set; }

    public double CompressorStrength { get; set; } = 35;

    public bool LimiterEnabled { get; set; }

    public double LimiterCeilingDb { get; set; } = -1;
}

public sealed class AudioClipDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Clip";

    /// <summary>
    /// Stored as an absolute path for external media or as a path relative to
    /// the .ewaudio file when SourcePathIsRelative is true.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    public bool SourcePathIsRelative { get; set; }

    public double TimelineStartSeconds { get; set; }

    public double SourceStartSeconds { get; set; }

    public double DurationSeconds { get; set; }

    public double GainDb { get; set; }

    public double FadeInSeconds { get; set; }

    public double FadeOutSeconds { get; set; }

    /// <summary>
    /// Non-destructive muted regions, measured in seconds relative to the clip start.
    /// They preserve timeline duration while suppressing only the selected audio.
    /// </summary>
    public List<AudioClipMuteRangeDocument> MutedRanges { get; set; } = [];

    public int SourceSampleRate { get; set; }

    public int SourceChannels { get; set; }

    public int SourceBitsPerSample { get; set; }

    public string SourceEncoding { get; set; } = string.Empty;
}


public sealed class AudioClipMuteRangeDocument
{
    public double StartSeconds { get; set; }

    public double DurationSeconds { get; set; }

    public double EndSeconds => StartSeconds + DurationSeconds;
}
