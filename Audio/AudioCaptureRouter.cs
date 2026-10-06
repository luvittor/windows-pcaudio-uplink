using NAudio.Wave;

namespace WindowsPcAudioUplink.Audio;

public sealed class AudioCaptureRouter : IDisposable
{
    readonly Func<AppSettings, Task<IAudioCaptureSource>> captureFactory;
    readonly Action<byte[], double> audioAvailable;
    readonly Action<Exception?> activeCaptureStopped;
    readonly SemaphoreSlim switchLock = new(1, 1);
    readonly int outputSampleRate;
    readonly int outputChannels;
    CaptureSession? active;
    bool disposed;

    public AudioCaptureRouter(
        int outputSampleRate,
        int outputChannels,
        Action<byte[], double> audioAvailable,
        Action<Exception?> activeCaptureStopped,
        Func<AppSettings, Task<IAudioCaptureSource>>? captureFactory = null)
    {
        this.outputSampleRate = outputSampleRate;
        this.outputChannels = outputChannels;
        this.audioAvailable = audioAvailable;
        this.activeCaptureStopped = activeCaptureStopped;
        this.captureFactory = captureFactory ?? AudioCaptureFactory.CreateAsync;
    }

    public string Description => active?.Source.Description ?? "sem captura";
    public WaveFormat? SourceFormat => active?.Source.WaveFormat;

    public async Task SwitchAsync(AppSettings settings)
    {
        await switchLock.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var source = await captureFactory(settings);
            var next = new CaptureSession(
                source,
                new PcmFrameConverter(source.WaveFormat, outputSampleRate, outputChannels),
                Math.Pow(10, settings.GainDb / 20.0));

            next.DataAvailable = OnDataAvailable;
            next.RecordingStopped = OnRecordingStopped;
            next.Attach();

            try
            {
                next.Source.StartRecording();
            }
            catch
            {
                next.Dispose();
                throw;
            }

            var previous = Interlocked.Exchange(ref active, next);
            previous?.Dispose();
        }
        finally
        {
            switchLock.Release();
        }
    }

    void OnDataAvailable(CaptureSession session, AudioDataAvailableEventArgs eventArgs)
    {
        if (!ReferenceEquals(Volatile.Read(ref active), session))
        {
            return;
        }

        var gained = AudioProcessing.ApplyGain(
            eventArgs.Buffer,
            eventArgs.BytesRecorded,
            session.Source.WaveFormat,
            session.GainMultiplier);
        var converted = session.Converter.Convert(gained, gained.Length);
        if (converted.Length > 0)
        {
            var level = AudioProcessing.EstimateLevel(
                converted,
                converted.Length,
                WaveFormat.CreateIeeeFloatWaveFormat(outputSampleRate, outputChannels));
            audioAvailable(converted, level);
        }
    }

    void OnRecordingStopped(CaptureSession session, StoppedEventArgs eventArgs)
    {
        if (ReferenceEquals(Volatile.Read(ref active), session))
        {
            activeCaptureStopped(eventArgs.Exception);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Interlocked.Exchange(ref active, null)?.Dispose();
        switchLock.Dispose();
    }

    sealed class CaptureSession : IDisposable
    {
        public CaptureSession(IAudioCaptureSource source, PcmFrameConverter converter, double gainMultiplier)
        {
            Source = source;
            Converter = converter;
            GainMultiplier = gainMultiplier;
        }

        public IAudioCaptureSource Source { get; }
        public PcmFrameConverter Converter { get; }
        public double GainMultiplier { get; }
        public Action<CaptureSession, AudioDataAvailableEventArgs>? DataAvailable { get; set; }
        public Action<CaptureSession, StoppedEventArgs>? RecordingStopped { get; set; }

        public void Attach()
        {
            Source.DataAvailable += HandleDataAvailable;
            Source.RecordingStopped += HandleRecordingStopped;
        }

        void HandleDataAvailable(object? sender, AudioDataAvailableEventArgs eventArgs)
        {
            DataAvailable?.Invoke(this, eventArgs);
        }

        void HandleRecordingStopped(object? sender, StoppedEventArgs eventArgs)
        {
            RecordingStopped?.Invoke(this, eventArgs);
        }

        public void Dispose()
        {
            Source.DataAvailable -= HandleDataAvailable;
            Source.RecordingStopped -= HandleRecordingStopped;
            try
            {
                Source.StopRecording();
            }
            catch
            {
                // A fonte pode ja ter encerrado durante uma troca.
            }

            Source.Dispose();
        }
    }
}
