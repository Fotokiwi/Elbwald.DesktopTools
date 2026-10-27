namespace Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;

internal readonly record struct TrackMusicalFxSettings(
    int ChannelMode,
    bool PhaseInvertLeft,
    bool PhaseInvertRight,
    bool SaturationEnabled,
    double SaturationDriveDb,
    double SaturationMixPercent,
    bool ChorusEnabled,
    double ChorusRateHz,
    double ChorusDepthPercent,
    double ChorusMixPercent,
    bool StereoWidthEnabled,
    double StereoWidthPercent,
    bool DelayEnabled,
    double DelayTimeMs,
    double DelayFeedbackPercent,
    double DelayMixPercent,
    bool DelayPingPong,
    bool ReverbEnabled,
    double ReverbMixPercent,
    double ReverbDecayPercent,
    double ReverbDampingPercent,
    double ReverbPreDelayMs);

/// <summary>
/// Musical insert stage for a track. Stereo width is a stateless mid/side
/// operation. Reverb is a compact Schroeder-style stereo room built from
/// parallel damped comb filters and serial all-pass diffusion stages.
/// </summary>
internal sealed class TrackMusicalFxProcessor
{
    private static readonly int[] CombTuningsLeft = [1116, 1188, 1277, 1356];
    private static readonly int[] CombTuningsRight = [1139, 1211, 1300, 1379];
    private static readonly int[] AllPassTuningsLeft = [556, 441];
    private static readonly int[] AllPassTuningsRight = [579, 464];

    private CombFilter[] _leftCombs = [];
    private CombFilter[] _rightCombs = [];
    private AllPassFilter[] _leftAllPasses = [];
    private AllPassFilter[] _rightAllPasses = [];
    private float[] _chorusLeft = [];
    private float[] _chorusRight = [];
    private int _chorusWriteIndex;
    private int _chorusSampleRate;
    private double _chorusPhase;
    private bool _chorusWasEnabled;

    private float[] _delayLeft = [];
    private float[] _delayRight = [];
    private int _delayWriteIndex;
    private int _delaySampleRate;
    private bool _delayWasEnabled;

    private float[] _preDelayLeft = [];
    private float[] _preDelayRight = [];
    private int _preDelayWriteIndex;
    private int _sampleRate;
    private bool _reverbWasEnabled;

    public void ProcessChannelTools(float[] interleavedStereo, TrackMusicalFxSettings settings)
    {
        if (interleavedStereo.Length < 2)
        {
            return;
        }

        var mode = Math.Clamp(settings.ChannelMode, 0, 4);
        var invertLeft = settings.PhaseInvertLeft ? -1f : 1f;
        var invertRight = settings.PhaseInvertRight ? -1f : 1f;

        for (var index = 0; index + 1 < interleavedStereo.Length; index += 2)
        {
            var left = interleavedStereo[index];
            var right = interleavedStereo[index + 1];

            switch (mode)
            {
                case 1: // Mono sum
                    var mono = (left + right) * 0.5f;
                    left = mono;
                    right = mono;
                    break;
                case 2: // Swap L/R
                    (left, right) = (right, left);
                    break;
                case 3: // Left to both
                    right = left;
                    break;
                case 4: // Right to both
                    left = right;
                    break;
            }

            interleavedStereo[index] = left * invertLeft;
            interleavedStereo[index + 1] = right * invertRight;
        }
    }

    public void ProcessSaturation(float[] interleavedStereo, TrackMusicalFxSettings settings)
    {
        if (!settings.SaturationEnabled || interleavedStereo.Length == 0)
        {
            return;
        }

        var driveDb = Math.Clamp(settings.SaturationDriveDb, 0d, 18d);
        var mix = Math.Clamp(settings.SaturationMixPercent, 0d, 100d) / 100d;
        if (driveDb <= 0.001d || mix <= 0.0001d)
        {
            return;
        }

        var drive = Math.Pow(10d, driveDb / 20d);
        var normalization = Math.Tanh(drive);
        if (normalization <= 1e-9)
        {
            return;
        }

        for (var index = 0; index < interleavedStereo.Length; index++)
        {
            var dry = interleavedStereo[index];
            var wet = Math.Tanh(dry * drive) / normalization;
            interleavedStereo[index] = (float)(dry + (wet - dry) * mix);
        }
    }

    public void ProcessChorus(float[] interleavedStereo, int sampleRate, TrackMusicalFxSettings settings)
    {
        if (interleavedStereo.Length < 2 || sampleRate <= 0)
        {
            return;
        }

        if (!settings.ChorusEnabled)
        {
            if (_chorusWasEnabled)
            {
                ResetChorus();
            }

            _chorusWasEnabled = false;
            return;
        }

        EnsureChorusState(sampleRate);
        _chorusWasEnabled = true;

        var rateHz = Math.Clamp(settings.ChorusRateHz, 0.05d, 3d);
        var depth = Math.Clamp(settings.ChorusDepthPercent, 0d, 100d) / 100d;
        var mix = Math.Clamp(settings.ChorusMixPercent, 0d, 100d) / 100d;
        var wetGain = Math.Sin(mix * Math.PI * 0.5d);
        var dryGain = Math.Cos(mix * Math.PI * 0.5d);
        var baseDelayMs = 12d + depth * 5d;
        var modulationMs = 1d + depth * 7d;
        var phaseStep = 2d * Math.PI * rateHz / sampleRate;

        for (var index = 0; index + 1 < interleavedStereo.Length; index += 2)
        {
            var dryLeft = interleavedStereo[index];
            var dryRight = interleavedStereo[index + 1];

            _chorusLeft[_chorusWriteIndex] = dryLeft;
            _chorusRight[_chorusWriteIndex] = dryRight;

            var leftDelayMs = Math.Clamp(
                baseDelayMs + Math.Sin(_chorusPhase) * modulationMs,
                1d,
                32d);
            var rightDelayMs = Math.Clamp(
                baseDelayMs + Math.Sin(_chorusPhase + Math.PI) * modulationMs,
                1d,
                32d);

            var wetLeft = ReadFractionalDelay(
                _chorusLeft,
                _chorusWriteIndex,
                leftDelayMs * sampleRate / 1_000d);
            var wetRight = ReadFractionalDelay(
                _chorusRight,
                _chorusWriteIndex,
                rightDelayMs * sampleRate / 1_000d);

            interleavedStereo[index] = (float)(dryLeft * dryGain + wetLeft * wetGain);
            interleavedStereo[index + 1] = (float)(dryRight * dryGain + wetRight * wetGain);

            _chorusWriteIndex++;
            if (_chorusWriteIndex >= _chorusLeft.Length)
            {
                _chorusWriteIndex = 0;
            }

            _chorusPhase += phaseStep;
            if (_chorusPhase >= 2d * Math.PI)
            {
                _chorusPhase -= 2d * Math.PI;
            }
        }
    }

    public void ProcessStereoWidth(float[] interleavedStereo, TrackMusicalFxSettings settings)
    {
        if (!settings.StereoWidthEnabled || interleavedStereo.Length < 2)
        {
            return;
        }

        var width = Math.Clamp(settings.StereoWidthPercent, 0d, 200d) / 100d;
        for (var index = 0; index + 1 < interleavedStereo.Length; index += 2)
        {
            var left = interleavedStereo[index];
            var right = interleavedStereo[index + 1];
            var mid = (left + right) * 0.5d;
            var side = (left - right) * 0.5d * width;
            interleavedStereo[index] = (float)(mid + side);
            interleavedStereo[index + 1] = (float)(mid - side);
        }
    }

    public void ProcessDelay(float[] interleavedStereo, int sampleRate, TrackMusicalFxSettings settings)
    {
        if (interleavedStereo.Length < 2 || sampleRate <= 0)
        {
            return;
        }

        if (!settings.DelayEnabled)
        {
            if (_delayWasEnabled)
            {
                ResetDelay();
            }

            _delayWasEnabled = false;
            return;
        }

        EnsureDelayState(sampleRate);
        _delayWasEnabled = true;

        var delayFrames = Math.Clamp(
            (int)Math.Round(Math.Clamp(settings.DelayTimeMs, 20d, 1_200d) * sampleRate / 1_000d),
            1,
            _delayLeft.Length - 1);
        var feedback = Math.Clamp(settings.DelayFeedbackPercent, 0d, 75d) / 100d;
        var mix = Math.Clamp(settings.DelayMixPercent, 0d, 100d) / 100d;
        var wetGain = Math.Sin(mix * Math.PI * 0.5d);
        var dryGain = Math.Cos(mix * Math.PI * 0.5d);

        for (var index = 0; index + 1 < interleavedStereo.Length; index += 2)
        {
            var dryLeft = interleavedStereo[index];
            var dryRight = interleavedStereo[index + 1];
            var readIndex = _delayWriteIndex - delayFrames;
            if (readIndex < 0)
            {
                readIndex += _delayLeft.Length;
            }

            var delayedLeft = _delayLeft[readIndex];
            var delayedRight = _delayRight[readIndex];

            if (settings.DelayPingPong)
            {
                _delayLeft[_delayWriteIndex] = (float)(dryLeft + delayedRight * feedback);
                _delayRight[_delayWriteIndex] = (float)(dryRight + delayedLeft * feedback);
            }
            else
            {
                _delayLeft[_delayWriteIndex] = (float)(dryLeft + delayedLeft * feedback);
                _delayRight[_delayWriteIndex] = (float)(dryRight + delayedRight * feedback);
            }

            _delayWriteIndex++;
            if (_delayWriteIndex >= _delayLeft.Length)
            {
                _delayWriteIndex = 0;
            }

            interleavedStereo[index] = (float)(dryLeft * dryGain + delayedLeft * wetGain);
            interleavedStereo[index + 1] = (float)(dryRight * dryGain + delayedRight * wetGain);
        }
    }

    public void ProcessReverb(float[] interleavedStereo, int sampleRate, TrackMusicalFxSettings settings)
    {
        if (interleavedStereo.Length < 2 || sampleRate <= 0)
        {
            return;
        }

        if (!settings.ReverbEnabled)
        {
            if (_reverbWasEnabled)
            {
                ResetReverb();
            }

            _reverbWasEnabled = false;
            return;
        }

        EnsureReverbState(sampleRate);
        _reverbWasEnabled = true;

        var mix = Math.Clamp(settings.ReverbMixPercent, 0d, 100d) / 100d;
        var wetGain = Math.Sin(mix * Math.PI * 0.5d);
        var dryGain = Math.Cos(mix * Math.PI * 0.5d);
        var decay = Math.Clamp(settings.ReverbDecayPercent, 0d, 100d) / 100d;
        var damping = Math.Clamp(settings.ReverbDampingPercent, 0d, 100d) / 100d;
        var feedback = 0.52d + decay * 0.40d;
        var dampingFactor = 0.08d + damping * 0.84d;
        var preDelayFrames = Math.Clamp(
            (int)Math.Round(Math.Clamp(settings.ReverbPreDelayMs, 0d, 120d) * sampleRate / 1000d),
            0,
            Math.Max(0, _preDelayLeft.Length - 1));

        for (var index = 0; index + 1 < interleavedStereo.Length; index += 2)
        {
            var dryLeft = interleavedStereo[index];
            var dryRight = interleavedStereo[index + 1];

            _preDelayLeft[_preDelayWriteIndex] = dryLeft;
            _preDelayRight[_preDelayWriteIndex] = dryRight;

            var readIndex = _preDelayWriteIndex - preDelayFrames;
            if (readIndex < 0)
            {
                readIndex += _preDelayLeft.Length;
            }

            var preLeft = preDelayFrames == 0 ? dryLeft : _preDelayLeft[readIndex];
            var preRight = preDelayFrames == 0 ? dryRight : _preDelayRight[readIndex];
            _preDelayWriteIndex++;
            if (_preDelayWriteIndex >= _preDelayLeft.Length)
            {
                _preDelayWriteIndex = 0;
            }

            // Feed a little opposite channel into each side to create a wider,
            // natural stereo tail without changing the dry stereo image.
            var inputLeft = preLeft * 0.78f + preRight * 0.22f;
            var inputRight = preRight * 0.78f + preLeft * 0.22f;

            double wetLeft = 0d;
            double wetRight = 0d;
            foreach (var comb in _leftCombs)
            {
                wetLeft += comb.Process(inputLeft, feedback, dampingFactor);
            }

            foreach (var comb in _rightCombs)
            {
                wetRight += comb.Process(inputRight, feedback, dampingFactor);
            }

            wetLeft /= Math.Max(1, _leftCombs.Length);
            wetRight /= Math.Max(1, _rightCombs.Length);

            foreach (var allPass in _leftAllPasses)
            {
                wetLeft = allPass.Process((float)wetLeft);
            }

            foreach (var allPass in _rightAllPasses)
            {
                wetRight = allPass.Process((float)wetRight);
            }

            interleavedStereo[index] = (float)(dryLeft * dryGain + wetLeft * wetGain);
            interleavedStereo[index + 1] = (float)(dryRight * dryGain + wetRight * wetGain);
        }
    }

    public void Reset()
    {
        ResetChorus();
        ResetDelay();
        ResetReverb();
        _chorusWasEnabled = false;
        _delayWasEnabled = false;
        _reverbWasEnabled = false;
    }

    private void EnsureChorusState(int sampleRate)
    {
        if (_chorusSampleRate == sampleRate && _chorusLeft.Length > 0)
        {
            return;
        }

        _chorusSampleRate = sampleRate;
        var length = Math.Max(64, (int)Math.Ceiling(sampleRate * 0.040d));
        _chorusLeft = new float[length];
        _chorusRight = new float[length];
        _chorusWriteIndex = 0;
        _chorusPhase = 0d;
    }

    private void ResetChorus()
    {
        Array.Clear(_chorusLeft);
        Array.Clear(_chorusRight);
        _chorusWriteIndex = 0;
        _chorusPhase = 0d;
    }

    private static float ReadFractionalDelay(float[] buffer, int writeIndex, double delayFrames)
    {
        if (buffer.Length < 3)
        {
            return 0f;
        }

        var readPosition = writeIndex - Math.Clamp(delayFrames, 1d, buffer.Length - 2d);
        while (readPosition < 0)
        {
            readPosition += buffer.Length;
        }

        var lower = (int)Math.Floor(readPosition) % buffer.Length;
        var upper = (lower + 1) % buffer.Length;
        var fraction = readPosition - Math.Floor(readPosition);
        return (float)(buffer[lower] + (buffer[upper] - buffer[lower]) * fraction);
    }

    private void EnsureDelayState(int sampleRate)
    {
        if (_delaySampleRate == sampleRate && _delayLeft.Length > 0)
        {
            return;
        }

        _delaySampleRate = sampleRate;
        var length = Math.Max(64, (int)Math.Ceiling(sampleRate * 1.25d));
        _delayLeft = new float[length];
        _delayRight = new float[length];
        _delayWriteIndex = 0;
    }

    private void ResetDelay()
    {
        Array.Clear(_delayLeft);
        Array.Clear(_delayRight);
        _delayWriteIndex = 0;
    }

    private void EnsureReverbState(int sampleRate)
    {
        if (_sampleRate == sampleRate && _leftCombs.Length > 0)
        {
            return;
        }

        _sampleRate = sampleRate;
        var scale = sampleRate / 44_100d;
        _leftCombs = CombTuningsLeft.Select(tuning => new CombFilter(ScaleDelay(tuning, scale))).ToArray();
        _rightCombs = CombTuningsRight.Select(tuning => new CombFilter(ScaleDelay(tuning, scale))).ToArray();
        _leftAllPasses = AllPassTuningsLeft.Select(tuning => new AllPassFilter(ScaleDelay(tuning, scale))).ToArray();
        _rightAllPasses = AllPassTuningsRight.Select(tuning => new AllPassFilter(ScaleDelay(tuning, scale))).ToArray();
        var maxPreDelayFrames = Math.Max(2, (int)Math.Ceiling(sampleRate * 0.125d));
        _preDelayLeft = new float[maxPreDelayFrames];
        _preDelayRight = new float[maxPreDelayFrames];
        _preDelayWriteIndex = 0;
    }

    private void ResetReverb()
    {
        foreach (var comb in _leftCombs) comb.Reset();
        foreach (var comb in _rightCombs) comb.Reset();
        foreach (var allPass in _leftAllPasses) allPass.Reset();
        foreach (var allPass in _rightAllPasses) allPass.Reset();
        Array.Clear(_preDelayLeft);
        Array.Clear(_preDelayRight);
        _preDelayWriteIndex = 0;
    }

    private static int ScaleDelay(int tuning, double scale) =>
        Math.Max(8, (int)Math.Round(tuning * scale));

    private sealed class CombFilter
    {
        private readonly float[] _buffer;
        private int _index;
        private double _filterStore;

        public CombFilter(int length)
        {
            _buffer = new float[Math.Max(8, length)];
        }

        public float Process(float input, double feedback, double damping)
        {
            var output = _buffer[_index];
            _filterStore = output * (1d - damping) + _filterStore * damping;
            _buffer[_index] = (float)(input + _filterStore * feedback);
            _index++;
            if (_index >= _buffer.Length) _index = 0;
            return output;
        }

        public void Reset()
        {
            Array.Clear(_buffer);
            _index = 0;
            _filterStore = 0d;
        }
    }

    private sealed class AllPassFilter
    {
        private readonly float[] _buffer;
        private int _index;

        public AllPassFilter(int length)
        {
            _buffer = new float[Math.Max(8, length)];
        }

        public float Process(float input)
        {
            var buffered = _buffer[_index];
            var output = -input + buffered;
            _buffer[_index] = input + buffered * 0.5f;
            _index++;
            if (_index >= _buffer.Length) _index = 0;
            return output;
        }

        public void Reset()
        {
            Array.Clear(_buffer);
            _index = 0;
        }
    }
}
