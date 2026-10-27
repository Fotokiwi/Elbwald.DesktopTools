using System.Text.Json;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.Playback;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

/// <summary>
/// Offline WAV mixdown. Rendering uses ProjectAudioMixerSession, therefore the
/// exported signal follows the same track/master DSP path as playback and
/// mastering analysis.
/// </summary>
public sealed class AudioWavExportService
{
    private const int RenderBlockFrames = 4096;
    private const double MaximumTailSeconds = 20d;

    private readonly WavMetadataReader _metadataReader;
    private readonly WavSampleDecoder _decoder;

    public AudioWavExportService(
        WavMetadataReader metadataReader,
        WavSampleDecoder decoder)
    {
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
    }

    public async Task<AudioWavExportResult> ExportAsync(
        AudioProjectDocument document,
        string filePath,
        AudioWavExportSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (settings.SampleRate is not (44_100 or 48_000 or 96_000))
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Für WAV-Export werden derzeit 44,1 kHz, 48 kHz oder 96 kHz unterstützt.");
        }

        var exportDocument = CloneDocument(document);
        exportDocument.SampleRate = settings.SampleRate;

        // Monitoring-only mode must never leak into an exported mix.
        foreach (var track in exportDocument.Tracks)
        {
            track.Effects.NoiseReductionListenRemoved = false;
        }

        var tailSeconds = settings.IncludeEffectTail
            ? EstimateEffectTailSeconds(exportDocument)
            : 0d;

        using var mixer = await ProjectAudioMixerSession.CreateAsync(
            exportDocument,
            _metadataReader,
            _decoder,
            cancellationToken,
            additionalTailSeconds: tailSeconds);

        if (!mixer.HasPlayableClips || mixer.DurationFrames <= 0)
        {
            throw new InvalidOperationException("Das Projekt enthält keine abspielbaren WAV-Clips.");
        }

        var fullPath = Path.GetFullPath(filePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            fullPath += ".wav";
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = fullPath + ".part";
        try
        {
            var result = await Task.Run(
                () => RenderAndWrite(
                    mixer,
                    temporaryPath,
                    fullPath,
                    settings,
                    tailSeconds,
                    progress,
                    cancellationToken),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
            return result with { FilePath = fullPath };
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public double EstimateEffectTailSeconds(AudioProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var longest = 0d;
        foreach (var track in document.Tracks)
        {
            var effects = track.Effects;

            if (effects.DelayEnabled && effects.DelayMixPercent > 0.01)
            {
                var delaySeconds = Math.Clamp(effects.DelayTimeMs, 20d, 1_200d) / 1_000d;
                var feedback = Math.Clamp(effects.DelayFeedbackPercent, 0d, 75d) / 100d;
                var repeats = feedback <= 0.001d
                    ? 1d
                    : Math.Ceiling(Math.Log(0.001d) / Math.Log(feedback));
                longest = Math.Max(longest, delaySeconds * Math.Clamp(repeats, 1d, 24d));
            }

            if (effects.ReverbEnabled && effects.ReverbMixPercent > 0.01)
            {
                var decay = Math.Clamp(effects.ReverbDecayPercent, 0d, 100d) / 100d;
                var preDelay = Math.Clamp(effects.ReverbPreDelayMs, 0d, 120d) / 1_000d;
                longest = Math.Max(longest, 1.6d + decay * 8.4d + preDelay);
            }
        }

        return Math.Clamp(longest <= 0d ? 0d : longest + 0.20d, 0d, MaximumTailSeconds);
    }

    private static AudioWavExportResult RenderAndWrite(
        ProjectAudioMixerSession mixer,
        string temporaryPath,
        string finalPath,
        AudioWavExportSettings settings,
        double tailSeconds,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var bytesPerSample = settings.Encoding switch
        {
            AudioWavEncoding.Pcm16 => 2,
            AudioWavEncoding.Pcm24 => 3,
            AudioWavEncoding.Float32 => 4,
            _ => throw new InvalidOperationException("Unbekanntes WAV-Encoding.")
        };

        const int channels = 2;
        var dataSize64 = checked(mixer.DurationFrames * channels * (long)bytesPerSample);
        if (dataSize64 > uint.MaxValue - 64L)
        {
            throw new NotSupportedException(
                "Der WAV-Export würde größer als 4 GB. RF64 wird in dieser Version noch nicht unterstützt.");
        }

        using var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            options: FileOptions.SequentialScan);
        using var writer = new BinaryWriter(stream);

        WriteHeader(
            writer,
            settings.SampleRate,
            settings.Encoding,
            checked((uint)dataSize64));

        var frame = 0L;
        var maxPeak = 0d;
        var samplesAboveFullScale = 0L;
        var dither = new TpdfDitherState(0x6D2B79F5u);

        progress?.Report(0d);

        while (frame < mixer.DurationFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = checked((int)Math.Min(RenderBlockFrames, mixer.DurationFrames - frame));
            var samples = mixer.Render(frame, count, cancellationToken);
            if (samples.Length == 0)
            {
                break;
            }

            foreach (var sample in samples)
            {
                var absolute = Math.Abs((double)sample);
                maxPeak = Math.Max(maxPeak, absolute);
                if (absolute > 1d)
                {
                    samplesAboveFullScale++;
                }

                switch (settings.Encoding)
                {
                    case AudioWavEncoding.Pcm16:
                        WritePcm16(writer, sample, settings.Dither, ref dither);
                        break;
                    case AudioWavEncoding.Pcm24:
                        WritePcm24(writer, sample, settings.Dither, ref dither);
                        break;
                    case AudioWavEncoding.Float32:
                        writer.Write(sample);
                        break;
                }
            }

            frame += samples.Length / channels;
            progress?.Report(Math.Clamp(frame / (double)mixer.DurationFrames, 0d, 1d));
        }

        writer.Flush();
        stream.Flush(flushToDisk: true);
        progress?.Report(1d);

        var peakDbfs = maxPeak <= 1e-12
            ? -120d
            : 20d * Math.Log10(maxPeak);

        return new AudioWavExportResult(
            finalPath,
            frame,
            settings.SampleRate,
            settings.Encoding,
            frame / (double)settings.SampleRate,
            peakDbfs,
            samplesAboveFullScale,
            tailSeconds);
    }

    private static void WriteHeader(
        BinaryWriter writer,
        int sampleRate,
        AudioWavEncoding encoding,
        uint dataSize)
    {
        const short channels = 2;
        var bitsPerSample = encoding switch
        {
            AudioWavEncoding.Pcm16 => (short)16,
            AudioWavEncoding.Pcm24 => (short)24,
            AudioWavEncoding.Float32 => (short)32,
            _ => throw new InvalidOperationException("Unbekanntes WAV-Encoding.")
        };
        var formatTag = encoding == AudioWavEncoding.Float32
            ? (short)3 // WAVE_FORMAT_IEEE_FLOAT
            : (short)1; // WAVE_FORMAT_PCM
        var blockAlign = checked((short)(channels * bitsPerSample / 8));
        var byteRate = checked(sampleRate * blockAlign);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(checked(36u + dataSize));
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16u);
        writer.Write(formatTag);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
    }

    private static void WritePcm16(
        BinaryWriter writer,
        float sample,
        bool ditherEnabled,
        ref TpdfDitherState dither)
    {
        const double scale = 32768d;
        var value = (double)sample;
        if (ditherEnabled)
        {
            value += dither.NextTpdf() / scale;
        }

        var quantized = (int)Math.Round(Math.Clamp(value, -1d, 32767d / scale) * scale);
        writer.Write((short)Math.Clamp(quantized, short.MinValue, short.MaxValue));
    }

    private static void WritePcm24(
        BinaryWriter writer,
        float sample,
        bool ditherEnabled,
        ref TpdfDitherState dither)
    {
        const double scale = 8_388_608d;
        var value = (double)sample;
        if (ditherEnabled)
        {
            value += dither.NextTpdf() / scale;
        }

        var quantized = (int)Math.Round(Math.Clamp(value, -1d, 8_388_607d / scale) * scale);
        quantized = Math.Clamp(quantized, -8_388_608, 8_388_607);

        writer.Write((byte)(quantized & 0xFF));
        writer.Write((byte)((quantized >> 8) & 0xFF));
        writer.Write((byte)((quantized >> 16) & 0xFF));
    }

    private static AudioProjectDocument CloneDocument(AudioProjectDocument document)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        return JsonSerializer.Deserialize<AudioProjectDocument>(bytes)
               ?? throw new InvalidDataException("Der Projektsnapshot konnte für den Export nicht erstellt werden.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private struct TpdfDitherState
    {
        private uint _state;

        public TpdfDitherState(uint seed)
        {
            _state = seed == 0 ? 1u : seed;
        }

        public double NextTpdf() => NextUniform() - NextUniform();

        private double NextUniform()
        {
            // Small deterministic xorshift PRNG; sufficient for TPDF quantization dither.
            var value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return value / (double)uint.MaxValue;
        }
    }
}
