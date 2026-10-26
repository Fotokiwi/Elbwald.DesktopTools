using Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.Services;
using MiniAudioEx.Core.AdvancedAPI;
using MiniAudioEx.Native;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Playback;

/// <summary>
/// Cross-platform realtime output service. miniaudio owns only the device;
/// project mixing and sample generation stay inside Elbwald.
/// </summary>
public sealed class AudioPlaybackService : IDisposable
{
    private const int OutputChannels = 2;
    private const int RenderBlockFrames = 2048;
    private const int PrebufferMilliseconds = 80;
    private const int TargetBufferMilliseconds = 120;
    private const int RingBufferSeconds = 1;

    private static AudioPlaybackService? _callbackOwner;

    private readonly WavMetadataReader _metadataReader;
    private readonly WavSampleDecoder _decoder;
    private readonly ma_device_data_proc _deviceDataCallback;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private MaDevice? _device;
    private int _deviceSampleRate;
    private StereoSampleRingBuffer? _ringBuffer;
    private ProjectAudioMixerSession? _mixer;
    private CancellationTokenSource? _producerCancellation;
    private Task? _producerTask;
    private Exception? _producerFault;
    private long _positionFrame;
    private long _durationFrames;
    private int _producerCompleted;
    private int _endPending;
    private int _underrunCount;
    private volatile AudioPlaybackState _state = AudioPlaybackState.Stopped;
    private Guid? _spectrumTargetTrackId;
    private bool _disposed;

    public AudioPlaybackService(WavMetadataReader metadataReader, WavSampleDecoder decoder)
    {
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _deviceDataCallback = OnDeviceData;
    }

    public AudioPlaybackState State => _state;

    public double PositionSeconds => _deviceSampleRate <= 0
        ? 0
        : Math.Clamp(Volatile.Read(ref _positionFrame) / (double)_deviceSampleRate, 0, DurationSeconds);

    public double DurationSeconds => _deviceSampleRate <= 0
        ? 0
        : Volatile.Read(ref _durationFrames) / (double)_deviceSampleRate;

    public int BufferedFrames => _ringBuffer?.AvailableFrames ?? 0;

    public double BufferedSeconds => _deviceSampleRate <= 0
        ? 0
        : BufferedFrames / (double)_deviceSampleRate;

    public int UnderrunCount => Volatile.Read(ref _underrunCount);

    public string? FaultMessage => _producerFault?.Message;

    public void UpdateTrackMixer(Guid trackId, bool isMuted, bool isSolo, double volumeDb, double pan)
    {
        var mixer = _mixer;
        mixer?.UpdateTrackMixer(trackId, isMuted, isSolo, volumeDb, pan);
    }

    public void UpdateTrackEffects(Guid trackId, AudioTrackEffectsDocument effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        var mixer = _mixer;
        mixer?.UpdateTrackEffects(trackId, effects);
    }

    public void UpdateMasterMixer(double volumeDb, bool limiterEnabled, double limiterCeilingDb)
    {
        var mixer = _mixer;
        mixer?.UpdateMasterMixer(volumeDb, limiterEnabled, limiterCeilingDb);
    }

    public void UpdateMasterEqualizer(AudioMasterEqualizerDocument equalizer)
    {
        ArgumentNullException.ThrowIfNull(equalizer);
        _mixer?.UpdateMasterEqualizer(equalizer);
    }

    internal MasterLoudnessSnapshot GetMasterLoudnessSnapshot()
    {
        var mixer = _mixer;
        return mixer?.GetMasterLoudnessSnapshot() ?? MasterLoudnessSnapshot.Empty;
    }

    internal MasterLimiterReductionSnapshot GetMasterLimiterReductionSnapshot()
    {
        var mixer = _mixer;
        return mixer?.GetMasterLimiterReductionSnapshot() ?? MasterLimiterReductionSnapshot.Empty;
    }

    internal void ResetMasterLoudnessAnalysis() =>
        _mixer?.ResetMasterLoudnessAnalysis();

    public AudioMeterSnapshot GetMeterSnapshot()
    {
        var mixer = _mixer;
        return mixer?.GetMeterSnapshot() ?? AudioMeterSnapshot.Empty;
    }

    public void SetSpectrumTarget(Guid? trackId)
    {
        _spectrumTargetTrackId = trackId;
        _mixer?.SetSpectrumTarget(trackId);
    }

    public AudioSpectrumSampleSnapshot GetSpectrumSampleSnapshot()
    {
        var mixer = _mixer;
        return mixer?.GetSpectrumSampleSnapshot() ?? AudioSpectrumSampleSnapshot.Empty;
    }

    public async Task PlayAsync(
        AudioProjectDocument document,
        double startSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (_state == AudioPlaybackState.Paused && _mixer is not null)
            {
                StartDeviceOrThrow();
                _state = AudioPlaybackState.Playing;
                return;
            }

            await StopCoreAsync(resetPosition: false);

            _state = AudioPlaybackState.Buffering;
            _producerFault = null;
            Volatile.Write(ref _producerCompleted, 0);
            Volatile.Write(ref _endPending, 0);
            Volatile.Write(ref _underrunCount, 0);

            var mixer = await ProjectAudioMixerSession.CreateAsync(
                document,
                _metadataReader,
                _decoder,
                cancellationToken);

            if (!mixer.HasPlayableClips || mixer.DurationFrames <= 0)
            {
                mixer.Dispose();
                _state = AudioPlaybackState.Stopped;
                throw new InvalidOperationException("Das Projekt enthält keine abspielbaren WAV-Clips.");
            }

            _mixer = mixer;
            mixer.SetSpectrumTarget(_spectrumTargetTrackId);
            EnsureDevice(mixer.SampleRate);
            _ringBuffer = new StereoSampleRingBuffer(checked(mixer.SampleRate * RingBufferSeconds));
            _durationFrames = mixer.DurationFrames;

            var startFrame = SecondsToFrame(startSeconds, mixer.SampleRate);
            if (startFrame >= mixer.DurationFrames)
            {
                startFrame = 0;
            }

            Volatile.Write(ref _positionFrame, startFrame);
            StartProducer(startFrame);
            await WaitForPrebufferAsync(cancellationToken);

            _callbackOwner = this;
            StartDeviceOrThrow();
            _state = AudioPlaybackState.Playing;
        }
        catch (Exception exception)
        {
            try
            {
                await StopCoreAsync(resetPosition: false);
            }
            catch
            {
                // Preserve the original startup error. A fresh Play call rebuilds the session.
            }

            _producerFault = exception;
            _state = AudioPlaybackState.Faulted;
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Pause()
    {
        ThrowIfDisposed();
        if (_state != AudioPlaybackState.Playing)
        {
            return;
        }

        _device?.Stop();
        _state = AudioPlaybackState.Paused;
    }

    public async Task SeekAsync(double positionSeconds, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (_mixer is null || _deviceSampleRate <= 0)
            {
                Volatile.Write(ref _positionFrame, 0);
                return;
            }

            var wasPlaying = _state == AudioPlaybackState.Playing;
            if (wasPlaying)
            {
                _device?.Stop();
            }

            await StopProducerAsync();
            _ringBuffer?.Reset();
            _mixer.ResetProcessingState();
            _producerFault = null;
            Volatile.Write(ref _producerCompleted, 0);
            Volatile.Write(ref _endPending, 0);

            var targetFrame = Math.Clamp(
                SecondsToFrame(positionSeconds, _deviceSampleRate),
                0,
                _durationFrames);
            Volatile.Write(ref _positionFrame, targetFrame);

            if (targetFrame < _durationFrames)
            {
                StartProducer(targetFrame);
                if (wasPlaying)
                {
                    _state = AudioPlaybackState.Buffering;
                    await WaitForPrebufferAsync(cancellationToken);
                    StartDeviceOrThrow();
                    _state = AudioPlaybackState.Playing;
                }
                else
                {
                    _state = AudioPlaybackState.Paused;
                }
            }
            else
            {
                _state = AudioPlaybackState.Stopped;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StopAsync(bool resetPosition = true)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync();
        try
        {
            await StopCoreAsync(resetPosition);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Called from the UI heartbeat. Native API calls are intentionally kept out
    /// of the miniaudio data callback.
    /// </summary>
    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        if (_producerFault is not null && _state is AudioPlaybackState.Playing or AudioPlaybackState.Buffering)
        {
            _device?.Stop();
            _state = AudioPlaybackState.Faulted;
            return;
        }

        if (Interlocked.Exchange(ref _endPending, 0) != 0)
        {
            _device?.Stop();
            Volatile.Write(ref _positionFrame, _durationFrames);
            _state = AudioPlaybackState.Stopped;
        }
    }

    private void EnsureDevice(int sampleRate)
    {
        if (_device is not null && _deviceSampleRate == sampleRate)
        {
            return;
        }

        _device?.Stop();
        _device?.Dispose();
        _device = null;

        var device = new MaDevice();
        var config = device.GetConfig(ma_device_type.playback);
        config.sampleRate = checked((uint)sampleRate);
        config.periodSizeInFrames = 512;
        config.playback.format = ma_format.f32;
        config.playback.channels = OutputChannels;
        config.SetDataCallback(_deviceDataCallback);

        var result = device.Initialize(config);
        if (result != ma_result.success)
        {
            device.Dispose();
            throw new InvalidOperationException($"Audio-Ausgabegerät konnte nicht initialisiert werden ({result}).");
        }

        _device = device;
        _deviceSampleRate = sampleRate;
        _callbackOwner = this;
    }

    private void StartProducer(long startFrame)
    {
        var mixer = _mixer ?? throw new InvalidOperationException("Playback-Mixer ist nicht initialisiert.");
        var ring = _ringBuffer ?? throw new InvalidOperationException("Playback-Puffer ist nicht initialisiert.");

        _producerCancellation = new CancellationTokenSource();
        var token = _producerCancellation.Token;
        _producerTask = Task.Run(async () =>
        {
            try
            {
                var renderFrame = startFrame;
                while (renderFrame < mixer.DurationFrames)
                {
                    token.ThrowIfCancellationRequested();

                    var targetBufferedFrames = MillisecondsToFrames(TargetBufferMilliseconds, mixer.SampleRate);
                    var availableFrames = ring.AvailableFrames;
                    if (availableFrames >= targetBufferedFrames)
                    {
                        await Task.Delay(3, token);
                        continue;
                    }

                    var freeFrames = ring.FreeFrames;
                    var framesUntilTarget = Math.Max(1, targetBufferedFrames - availableFrames);
                    var blockFrames = checked((int)Math.Min(
                        Math.Min(Math.Min(RenderBlockFrames, freeFrames), framesUntilTarget),
                        mixer.DurationFrames - renderFrame));
                    var samples = mixer.Render(renderFrame, blockFrames, token);
                    if (samples.Length == 0)
                    {
                        break;
                    }

                    var writtenFrames = ring.Write(samples);
                    renderFrame += writtenFrames;
                    if (writtenFrames == 0)
                    {
                        await Task.Delay(2, token);
                    }
                }

                if (renderFrame >= mixer.DurationFrames)
                {
                    mixer.CompleteMasterAnalysis();
                }

                Volatile.Write(ref _producerCompleted, 1);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _producerFault = exception;
                Volatile.Write(ref _producerCompleted, 1);
            }
        }, token);
    }

    private async Task WaitForPrebufferAsync(CancellationToken cancellationToken)
    {
        var ring = _ringBuffer ?? throw new InvalidOperationException("Playback-Puffer ist nicht initialisiert.");

        var prebufferFrames = MillisecondsToFrames(PrebufferMilliseconds, _deviceSampleRate);
        while (ring.AvailableFrames < prebufferFrames &&
               Volatile.Read(ref _producerCompleted) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_producerFault is not null)
            {
                throw new InvalidOperationException("Audiopuffer konnte nicht aufgebaut werden.", _producerFault);
            }

            await Task.Delay(5, cancellationToken);
        }

        if (_producerFault is not null)
        {
            throw new InvalidOperationException("Audiopuffer konnte nicht aufgebaut werden.", _producerFault);
        }
    }

    private async Task StopCoreAsync(bool resetPosition)
    {
        _device?.Stop();
        await StopProducerAsync();
        _ringBuffer?.Reset();
        _ringBuffer = null;

        _mixer?.Dispose();
        _mixer = null;
        _producerFault = null;
        Volatile.Write(ref _producerCompleted, 0);
        Volatile.Write(ref _endPending, 0);
        Volatile.Write(ref _durationFrames, 0);
        if (resetPosition)
        {
            Volatile.Write(ref _positionFrame, 0);
        }

        _state = AudioPlaybackState.Stopped;
    }

    private async Task StopProducerAsync()
    {
        var cancellation = _producerCancellation;
        var task = _producerTask;
        _producerCancellation = null;
        _producerTask = null;

        if (cancellation is not null)
        {
            cancellation.Cancel();
        }

        if (task is not null)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
    }

    private static unsafe void OnDeviceData(
        ma_device_ptr pDevice,
        IntPtr pOutput,
        IntPtr pInput,
        uint frameCount)
    {
        if (pOutput == IntPtr.Zero || frameCount == 0)
        {
            return;
        }

        var owner = Volatile.Read(ref _callbackOwner);
        var sampleCount = checked((int)frameCount * OutputChannels);
        var destination = new Span<float>(pOutput.ToPointer(), sampleCount);

        if (owner is null || owner._state != AudioPlaybackState.Playing)
        {
            destination.Clear();
            return;
        }

        owner.FillOutput(destination, checked((int)frameCount));
    }

    private void FillOutput(Span<float> destination, int requestedFrames)
    {
        var ring = _ringBuffer;
        if (ring is null)
        {
            destination.Clear();
            return;
        }

        var framesRead = ring.Read(destination, requestedFrames);
        if (framesRead > 0)
        {
            Interlocked.Add(ref _positionFrame, framesRead);
        }

        if (framesRead < requestedFrames && Volatile.Read(ref _producerCompleted) == 0)
        {
            Interlocked.Increment(ref _underrunCount);
        }

        var position = Volatile.Read(ref _positionFrame);
        if (position >= Volatile.Read(ref _durationFrames) ||
            (framesRead == 0 && Volatile.Read(ref _producerCompleted) != 0 && ring.AvailableFrames == 0))
        {
            Volatile.Write(ref _endPending, 1);
        }
    }

    private void StartDeviceOrThrow()
    {
        var device = _device ?? throw new InvalidOperationException("Audio-Ausgabegerät ist nicht initialisiert.");
        var result = device.Start();
        if (result != ma_result.success)
        {
            throw new InvalidOperationException($"Audio-Ausgabegerät konnte nicht gestartet werden ({result}).");
        }
    }

    private static long SecondsToFrame(double seconds, int sampleRate) =>
        checked((long)Math.Round(Math.Max(0, seconds) * sampleRate, MidpointRounding.AwayFromZero));

    private static int MillisecondsToFrames(int milliseconds, int sampleRate) =>
        Math.Max(1, checked((int)Math.Ceiling(milliseconds / 1000d * sampleRate)));

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioPlaybackService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _device?.Stop();
        _producerCancellation?.Cancel();
        try
        {
            _producerTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _producerCancellation?.Dispose();
        _mixer?.Dispose();
        _device?.Dispose();
        _device = null;
        _mixer = null;
        _ringBuffer = null;
        if (ReferenceEquals(_callbackOwner, this))
        {
            _callbackOwner = null;
        }

        _operationGate.Dispose();
    }
}
