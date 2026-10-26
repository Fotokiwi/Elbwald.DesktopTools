using System.Numerics;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;

internal readonly record struct TrackNoiseReductionSettings(
    bool Enabled,
    double Strength,
    int ProfileSampleRate,
    int ProfileFftSize,
    double[] ProfileDb,
    bool ListenRemovedNoise)
{
    public bool HasProfile =>
        ProfileSampleRate > 0 &&
        ProfileFftSize > 0 &&
        ProfileDb is { Length: > 1 };
}

/// <summary>
/// Conservative spectral denoiser. It applies the same frequency-dependent gain
/// to both stereo channels to preserve imaging. Processing is block-local WOLA,
/// therefore it introduces no timeline delay and is safe to insert per track.
/// </summary>
internal sealed class SpectralNoiseReducer
{
    private const int FftSize = 1024;
    private const int HopSize = FftSize / 4;
    private readonly double[] _window = BuildHannWindow(FftSize);
    private readonly Complex[] _leftSpectrum = new Complex[FftSize];
    private readonly Complex[] _rightSpectrum = new Complex[FftSize];
    private readonly double[] _gains = new double[FftSize / 2 + 1];
    private readonly double[] _smoothedGains = new double[FftSize / 2 + 1];
    private double[] _leftOutput = [];
    private double[] _rightOutput = [];
    private double[] _normalization = [];
    private readonly double _windowSum;

    public SpectralNoiseReducer()
    {
        _windowSum = _window.Sum();
    }

    public void Process(float[] interleavedStereo, int sampleRate, TrackNoiseReductionSettings settings)
    {
        if (interleavedStereo.Length < 2 ||
            sampleRate <= 0 ||
            !settings.Enabled ||
            !settings.HasProfile ||
            settings.Strength <= 0.01)
        {
            return;
        }

        var frameCount = interleavedStereo.Length / 2;
        if (frameCount <= 0)
        {
            return;
        }

        EnsureOutputCapacity(frameCount);
        Array.Clear(_leftOutput, 0, frameCount);
        Array.Clear(_rightOutput, 0, frameCount);
        Array.Clear(_normalization, 0, frameCount);

        var strength = Math.Clamp(settings.Strength, 0, 100) / 100d;
        var maximumReductionDb = 6d + 30d * strength;
        var transitionDb = 8d + 8d * (1d - strength);
        var minimumGain = DbToLinear(-maximumReductionDb);
        var magnitudeNormalization = _windowSum > 0 ? 2d / _windowSum : 1d / FftSize;

        // Negative starts guarantee window coverage at the beginning of every render block.
        for (var frameStart = -FftSize + HopSize; frameStart < frameCount; frameStart += HopSize)
        {
            Array.Clear(_leftSpectrum);
            Array.Clear(_rightSpectrum);

            for (var index = 0; index < FftSize; index++)
            {
                var sourceFrame = frameStart + index;
                if ((uint)sourceFrame >= (uint)frameCount)
                {
                    continue;
                }

                var sourceIndex = sourceFrame * 2;
                var window = _window[index];
                _leftSpectrum[index] = new Complex(interleavedStereo[sourceIndex] * window, 0);
                _rightSpectrum[index] = new Complex(interleavedStereo[sourceIndex + 1] * window, 0);
            }

            FftUtility.Forward(_leftSpectrum);
            FftUtility.Forward(_rightSpectrum);

            var binCount = _gains.Length;
            for (var bin = 0; bin < binCount; bin++)
            {
                var frequency = bin * sampleRate / (double)FftSize;
                var leftMagnitude = _leftSpectrum[bin].Magnitude * magnitudeNormalization;
                var rightMagnitude = _rightSpectrum[bin].Magnitude * magnitudeNormalization;
                var linkedMagnitude = Math.Max(leftMagnitude, rightMagnitude);
                var signalDb = 20d * Math.Log10(Math.Max(1e-10, linkedMagnitude));
                var noiseDb = SampleProfileDb(settings, frequency);
                var signalAboveNoiseDb = signalDb - noiseDb;

                // Full attenuation at/under the learned floor, tapering smoothly to unity.
                var noiseDominance = Math.Clamp((transitionDb - signalAboveNoiseDb) / transitionDb, 0d, 1d);
                noiseDominance *= noiseDominance;
                var reductionDb = maximumReductionDb * noiseDominance;
                _gains[bin] = Math.Max(minimumGain, DbToLinear(-reductionDb));
            }

            // Small frequency-domain smoothing reduces musical-noise speckles.
            for (var bin = 0; bin < _gains.Length; bin++)
            {
                var sum = 0d;
                var weight = 0d;
                for (var offset = -2; offset <= 2; offset++)
                {
                    var candidate = bin + offset;
                    if ((uint)candidate >= (uint)_gains.Length)
                    {
                        continue;
                    }

                    var localWeight = offset == 0 ? 3d : Math.Abs(offset) == 1 ? 2d : 1d;
                    sum += _gains[candidate] * localWeight;
                    weight += localWeight;
                }

                _smoothedGains[bin] = weight > 0 ? sum / weight : _gains[bin];
            }

            for (var bin = 0; bin < _gains.Length; bin++)
            {
                var gain = Math.Clamp(_smoothedGains[bin], minimumGain, 1d);
                var appliedGain = settings.ListenRemovedNoise ? 1d - gain : gain;
                _leftSpectrum[bin] *= appliedGain;
                _rightSpectrum[bin] *= appliedGain;

                if (bin == 0 || bin == FftSize / 2)
                {
                    continue;
                }

                var mirror = FftSize - bin;
                _leftSpectrum[mirror] *= appliedGain;
                _rightSpectrum[mirror] *= appliedGain;
            }

            FftUtility.Inverse(_leftSpectrum);
            FftUtility.Inverse(_rightSpectrum);

            for (var index = 0; index < FftSize; index++)
            {
                var destinationFrame = frameStart + index;
                if ((uint)destinationFrame >= (uint)frameCount)
                {
                    continue;
                }

                var window = _window[index];
                var synthesisWeight = window * window;
                _leftOutput[destinationFrame] += _leftSpectrum[index].Real * window;
                _rightOutput[destinationFrame] += _rightSpectrum[index].Real * window;
                _normalization[destinationFrame] += synthesisWeight;
            }
        }

        for (var frame = 0; frame < frameCount; frame++)
        {
            var norm = _normalization[frame];
            if (norm <= 1e-12)
            {
                continue;
            }

            var index = frame * 2;
            interleavedStereo[index] = (float)Math.Clamp(_leftOutput[frame] / norm, -4d, 4d);
            interleavedStereo[index + 1] = (float)Math.Clamp(_rightOutput[frame] / norm, -4d, 4d);
        }
    }

    public void Reset()
    {
        // Block-local WOLA is intentionally stateless so tracks keep sample alignment.
    }

    private void EnsureOutputCapacity(int frameCount)
    {
        if (_leftOutput.Length >= frameCount)
        {
            return;
        }

        _leftOutput = new double[frameCount];
        _rightOutput = new double[frameCount];
        _normalization = new double[frameCount];
    }

    private static double SampleProfileDb(TrackNoiseReductionSettings settings, double frequency)
    {
        if (!settings.HasProfile || frequency < 0)
        {
            return -120;
        }

        var exactBin = frequency * settings.ProfileFftSize / settings.ProfileSampleRate;
        if (exactBin <= 0)
        {
            return settings.ProfileDb[0];
        }

        if (exactBin >= settings.ProfileDb.Length - 1)
        {
            return settings.ProfileDb[^1];
        }

        var lower = (int)Math.Floor(exactBin);
        var upper = Math.Min(settings.ProfileDb.Length - 1, lower + 1);
        var fraction = exactBin - lower;
        return settings.ProfileDb[lower] + (settings.ProfileDb[upper] - settings.ProfileDb[lower]) * fraction;
    }

    private static double[] BuildHannWindow(int length)
    {
        var window = new double[length];
        for (var index = 0; index < length; index++)
        {
            window[index] = 0.5d - 0.5d * Math.Cos(2d * Math.PI * index / (length - 1));
        }

        return window;
    }

    private static double DbToLinear(double db) => Math.Pow(10d, db / 20d);
}
