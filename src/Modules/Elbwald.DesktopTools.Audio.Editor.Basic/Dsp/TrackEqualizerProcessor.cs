namespace Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;

internal readonly record struct TrackEqualizerSettings(
    bool EqualizerEnabled,
    double BassDb,
    double MidDb,
    double MidFrequencyHz,
    double TrebleDb,
    bool HighPassEnabled,
    double HighPassFrequencyHz,
    bool LowPassEnabled,
    double LowPassFrequencyHz);

internal sealed class TrackEqualizerProcessor
{
    private readonly BiquadStereoSection _highPass = new();
    private readonly BiquadStereoSection _lowShelf = new();
    private readonly BiquadStereoSection _midPeak = new();
    private readonly BiquadStereoSection _highShelf = new();
    private readonly BiquadStereoSection _lowPass = new();

    public void Process(float[] interleavedStereo, int sampleRate, TrackEqualizerSettings settings)
    {
        if (interleavedStereo.Length == 0)
        {
            return;
        }

        _highPass.Configure(
            settings.HighPassEnabled,
            BiquadDesign.HighPass(sampleRate, settings.HighPassFrequencyHz));
        _lowShelf.Configure(
            settings.EqualizerEnabled && Math.Abs(settings.BassDb) > 0.01,
            BiquadDesign.LowShelf(sampleRate, 120, settings.BassDb));
        _midPeak.Configure(
            settings.EqualizerEnabled && Math.Abs(settings.MidDb) > 0.01,
            BiquadDesign.Peaking(sampleRate, settings.MidFrequencyHz, settings.MidDb, 0.9));
        _highShelf.Configure(
            settings.EqualizerEnabled && Math.Abs(settings.TrebleDb) > 0.01,
            BiquadDesign.HighShelf(sampleRate, 8_000, settings.TrebleDb));
        _lowPass.Configure(
            settings.LowPassEnabled,
            BiquadDesign.LowPass(sampleRate, settings.LowPassFrequencyHz));

        _highPass.Process(interleavedStereo);
        _lowShelf.Process(interleavedStereo);
        _midPeak.Process(interleavedStereo);
        _highShelf.Process(interleavedStereo);
        _lowPass.Process(interleavedStereo);
    }

    public void Reset()
    {
        _highPass.Reset();
        _lowShelf.Reset();
        _midPeak.Reset();
        _highShelf.Reset();
        _lowPass.Reset();
    }
}
