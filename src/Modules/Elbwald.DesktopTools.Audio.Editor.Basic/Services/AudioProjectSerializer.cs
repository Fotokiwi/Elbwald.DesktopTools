using System.Text.Json;
using System.Text.Json.Serialization;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Contracts.Media.Audio;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

public sealed class AudioProjectSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public async Task SaveAsync(
        string projectPath,
        AudioProjectDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(document);

        var fullProjectPath = EnsureProjectExtension(Path.GetFullPath(projectPath));
        var projectDirectory = Path.GetDirectoryName(fullProjectPath)
            ?? throw new InvalidOperationException("Der Projektordner konnte nicht bestimmt werden.");

        Directory.CreateDirectory(projectDirectory);

        var portable = CloneForSave(document, projectDirectory);
        var temporaryPath = fullProjectPath + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 32 * 1024,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    portable,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, fullProjectPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<AudioProjectDocument> LoadAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var fullProjectPath = Path.GetFullPath(projectPath);
        var projectDirectory = Path.GetDirectoryName(fullProjectPath)
            ?? throw new InvalidOperationException("Der Projektordner konnte nicht bestimmt werden.");

        await using var stream = new FileStream(
            fullProjectPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 32 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var document = await JsonSerializer.DeserializeAsync<AudioProjectDocument>(
            stream,
            SerializerOptions,
            cancellationToken)
            ?? throw new InvalidDataException("Die Projektdatei enthält kein gültiges Audio-Projekt.");

        MigrateToCurrentVersion(document);
        Validate(document);
        ResolveSourcePaths(document, projectDirectory);
        return document;
    }


    private static void MigrateToCurrentVersion(AudioProjectDocument document)
    {
        if (!string.Equals(document.Format, AudioProjectDocument.CurrentFormat, StringComparison.Ordinal))
        {
            return;
        }

        if (document.Version == 1)
        {
            // V1 stored a track limiter switch that was only a UI placeholder and
            // defaulted to true. Activating it during an upgrade would therefore
            // silently change every existing mix. V2 starts the now-functional
            // limiter explicitly disabled; users can opt in per track.
            foreach (var track in document.Tracks)
            {
                track.Effects.LimiterEnabled = false;
            }

            document.Version = 2;
        }

        if (document.Version == 2)
        {
            // V3 turns the formerly placeholder-only noise-reduction switch into
            // real DSP. Disable the old switch during migration so existing mixes
            // cannot change sound merely because the editor was upgraded.
            foreach (var track in document.Tracks)
            {
                track.Effects.NoiseReductionEnabled = false;
                track.Effects.NoiseProfileSampleRate = 0;
                track.Effects.NoiseProfileFftSize = 0;
                track.Effects.NoiseProfileDb = [];
            }

            document.Version = 3;
        }

        if (document.Version == 3)
        {
            // V4 activates the master true-peak limiter. In V3 the switch was a
            // UI placeholder and defaulted to true, so existing projects must not
            // suddenly change sound after an upgrade.
            document.Master.LimiterEnabled = false;
            document.Version = 4;
        }

        if (document.Version == 4)
        {
            // V5 adds a real master EQ/filter chain. Defaults are neutral so
            // existing projects remain bit-for-bit unchanged until the user opts in.
            document.Master.Equalizer ??= new AudioMasterEqualizerDocument();
            document.Version = 5;
        }

        if (document.Version == 5)
        {
            // V6 adds basic per-track cleanup FX (expander/gate, notch, hum removal
            // and de-esser). Every new effect defaults to disabled, therefore old
            // projects retain their exact mix until the user explicitly enables one.
            document.Version = AudioProjectDocument.CurrentVersion;
        }
    }

    private static void Validate(AudioProjectDocument document)
    {
        if (!string.Equals(
                document.Format,
                AudioProjectDocument.CurrentFormat,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Die Datei ist kein Elbwald-Audio-Projekt.");
        }

        if (document.Version != AudioProjectDocument.CurrentVersion)
        {
            throw new NotSupportedException(
                $"Projektversion {document.Version} wird von diesem Editor nicht unterstützt. Erwartet wird Version {AudioProjectDocument.CurrentVersion}.");
        }

        if (document.Tracks.Count > AudioProjectLimits.MaxTrackCount)
        {
            throw new InvalidDataException(
                $"Das Projekt enthält {document.Tracks.Count} Spuren. Unterstützt werden maximal {AudioProjectLimits.MaxTrackCount}.");
        }

        if (document.SampleRate is < 8_000 or > 384_000)
        {
            throw new InvalidDataException("Die Projekt-Samplerate liegt außerhalb des unterstützten Bereichs.");
        }

        foreach (var track in document.Tracks)
        {
            if (track.ChannelLayout is not AudioChannelLayout.Mono and not AudioChannelLayout.Stereo)
            {
                throw new InvalidDataException("Das Projekt enthält ein nicht unterstütztes Kanalformat.");
            }

            if (track.Effects.NoiseProfileDb is { Length: > 32769 })
            {
                throw new InvalidDataException("Ein Rauschprofil ist unerwartet groß.");
            }

            foreach (var clip in track.Clips)
            {
                if (string.IsNullOrWhiteSpace(clip.SourcePath))
                {
                    throw new InvalidDataException("Ein Audio-Clip besitzt keinen Quellpfad.");
                }

                if (clip.TimelineStartSeconds < 0 ||
                    clip.SourceStartSeconds < 0 ||
                    clip.DurationSeconds < 0)
                {
                    throw new InvalidDataException("Ein Audio-Clip enthält negative Zeitwerte.");
                }
            }
        }
    }

    private static AudioProjectDocument CloneForSave(
        AudioProjectDocument source,
        string projectDirectory)
    {
        var clone = new AudioProjectDocument
        {
            Format = AudioProjectDocument.CurrentFormat,
            Version = AudioProjectDocument.CurrentVersion,
            Name = source.Name,
            SampleRate = source.SampleRate,
            Master = new AudioMasterDocument
            {
                VolumeDb = source.Master.VolumeDb,
                TargetLufs = source.Master.TargetLufs,
                LimiterEnabled = source.Master.LimiterEnabled,
                LimiterCeilingDb = source.Master.LimiterCeilingDb,
                Equalizer = new AudioMasterEqualizerDocument
                {
                    EqualizerEnabled = source.Master.Equalizer.EqualizerEnabled,
                    BassDb = source.Master.Equalizer.BassDb,
                    MidDb = source.Master.Equalizer.MidDb,
                    MidFrequencyHz = source.Master.Equalizer.MidFrequencyHz,
                    TrebleDb = source.Master.Equalizer.TrebleDb,
                    HighPassEnabled = source.Master.Equalizer.HighPassEnabled,
                    HighPassFrequencyHz = source.Master.Equalizer.HighPassFrequencyHz,
                    LowPassEnabled = source.Master.Equalizer.LowPassEnabled,
                    LowPassFrequencyHz = source.Master.Equalizer.LowPassFrequencyHz
                }
            }
        };

        foreach (var track in source.Tracks)
        {
            var trackClone = new AudioTrackDocument
            {
                Id = track.Id,
                Name = track.Name,
                ChannelLayout = track.ChannelLayout,
                IsMuted = track.IsMuted,
                IsSolo = track.IsSolo,
                VolumeDb = track.VolumeDb,
                Pan = track.Pan,
                Effects = new AudioTrackEffectsDocument
                {
                    NoiseReductionEnabled = track.Effects.NoiseReductionEnabled,
                    NoiseReductionStrength = track.Effects.NoiseReductionStrength,
                    NoiseProfileSampleRate = track.Effects.NoiseProfileSampleRate,
                    NoiseProfileFftSize = track.Effects.NoiseProfileFftSize,
                    NoiseProfileDb = track.Effects.NoiseProfileDb?.ToArray() ?? [],
                    ExpanderEnabled = track.Effects.ExpanderEnabled,
                    ExpanderThresholdDb = track.Effects.ExpanderThresholdDb,
                    ExpanderReductionDb = track.Effects.ExpanderReductionDb,
                    NotchEnabled = track.Effects.NotchEnabled,
                    NotchFrequencyHz = track.Effects.NotchFrequencyHz,
                    NotchWidthPercent = track.Effects.NotchWidthPercent,
                    HumRemovalEnabled = track.Effects.HumRemovalEnabled,
                    HumFrequencyHz = track.Effects.HumFrequencyHz,
                    HumHarmonics = track.Effects.HumHarmonics,
                    HumReductionDb = track.Effects.HumReductionDb,
                    DeEsserEnabled = track.Effects.DeEsserEnabled,
                    DeEsserFrequencyHz = track.Effects.DeEsserFrequencyHz,
                    DeEsserStrength = track.Effects.DeEsserStrength,
                    EqualizerEnabled = track.Effects.EqualizerEnabled,
                    BassDb = track.Effects.BassDb,
                    MidDb = track.Effects.MidDb,
                    TrebleDb = track.Effects.TrebleDb,
                    MidFrequencyHz = track.Effects.MidFrequencyHz,
                    HighPassEnabled = track.Effects.HighPassEnabled,
                    HighPassFrequencyHz = track.Effects.HighPassFrequencyHz,
                    LowPassEnabled = track.Effects.LowPassEnabled,
                    LowPassFrequencyHz = track.Effects.LowPassFrequencyHz,
                    CompressorEnabled = track.Effects.CompressorEnabled,
                    CompressorStrength = track.Effects.CompressorStrength,
                    LimiterEnabled = track.Effects.LimiterEnabled,
                    LimiterCeilingDb = track.Effects.LimiterCeilingDb
                }
            };

            foreach (var clip in track.Clips)
            {
                var sourcePath = Path.GetFullPath(clip.SourcePath);
                var portablePath = MakePortableSourcePath(projectDirectory, sourcePath);

                trackClone.Clips.Add(
                    new AudioClipDocument
                    {
                        Id = clip.Id,
                        Name = clip.Name,
                        SourcePath = portablePath.Path,
                        SourcePathIsRelative = portablePath.IsRelative,
                        TimelineStartSeconds = clip.TimelineStartSeconds,
                        SourceStartSeconds = clip.SourceStartSeconds,
                        DurationSeconds = clip.DurationSeconds,
                        GainDb = clip.GainDb,
                        FadeInSeconds = clip.FadeInSeconds,
                        FadeOutSeconds = clip.FadeOutSeconds,
                        SourceSampleRate = clip.SourceSampleRate,
                        SourceChannels = clip.SourceChannels,
                        SourceBitsPerSample = clip.SourceBitsPerSample,
                        SourceEncoding = clip.SourceEncoding
                    });
            }

            clone.Tracks.Add(trackClone);
        }

        return clone;
    }

    private static (string Path, bool IsRelative) MakePortableSourcePath(
        string projectDirectory,
        string sourcePath)
    {
        var relativePath = Path.GetRelativePath(projectDirectory, sourcePath);

        if (!Path.IsPathRooted(relativePath) &&
            !string.Equals(relativePath, "..", StringComparison.Ordinal) &&
            !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return (ToPortableRelativePath(relativePath), true);
        }

        return (sourcePath, false);
    }

    private static void ResolveSourcePaths(
        AudioProjectDocument document,
        string projectDirectory)
    {
        foreach (var clip in document.Tracks.SelectMany(track => track.Clips))
        {
            var resolved = clip.SourcePathIsRelative
                ? Path.Combine(projectDirectory, FromPortableRelativePath(clip.SourcePath))
                : clip.SourcePath;

            clip.SourcePath = Path.GetFullPath(resolved);
            clip.SourcePathIsRelative = false;
        }
    }

    private static string ToPortableRelativePath(string relativePath) =>
        relativePath
            .Replace('\\', '/')
            .Replace(Path.DirectorySeparatorChar, '/');

    private static string FromPortableRelativePath(string relativePath) =>
        relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

    private static string EnsureProjectExtension(string projectPath) =>
        string.Equals(Path.GetExtension(projectPath), ".ewaudio", StringComparison.OrdinalIgnoreCase)
            ? projectPath
            : projectPath + ".ewaudio";
}
