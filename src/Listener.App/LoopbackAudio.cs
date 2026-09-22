using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Listener.App;

public sealed record OutputDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

internal sealed record AudioCaptureHealth(long Packets, double? LastPacketAgeSeconds, double Rms, double? LastSoundAgeSeconds);

internal interface IPlaybackCapture : IAsyncDisposable
{
    AudioTimeline Timeline { get; }
    string DeviceId { get; }
    string DeviceName { get; }
    bool Running { get; }
    event Action<string>? Failed;
    AudioCaptureHealth Health();
    string Resolve(string requested);
    void Start(string requested);
    Task Stop();
}

internal sealed class LoopbackAudio : IPlaybackCapture
{
    private readonly MMDeviceEnumerator enumerator = new();
    private MMDevice? device;
    private WasapiRecorder? recorder;
    public AudioTimeline Timeline { get; private set; } = new();
    public string DeviceId { get; private set; } = "";
    public string DeviceName { get; private set; } = "";
    public bool Running => recorder is not null;
    public event Action<string>? Failed;
    private readonly object healthSync = new();
    private long packets;
    private double? lastPacketAt, lastSoundAt;
    private double rms;
    public long Packets { get { lock (healthSync) return packets; } }
    public AudioCaptureHealth Health()
    {
        lock (healthSync) return new(packets, lastPacketAt is { } p ? NativeInput.Now - p : null,
            rms, lastSoundAt is { } s ? NativeInput.Now - s : null);
    }
    public static List<OutputDevice> Devices()
    {
        using var e = new MMDeviceEnumerator();
        var result = new List<OutputDevice> { new("", "跟随系统默认播放设备") };
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        { using (d) result.Add(new(d.ID, d.FriendlyName)); }
        return result;
    }
    public string Resolve(string requested)
    {
        if (!string.IsNullOrEmpty(requested)) return requested;
        using var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return d.ID;
    }
    public void Start(string requested)
    {
        if (Running) throw new InvalidOperationException("采音已启动。");
        device = enumerator.GetDevice(Resolve(requested));
        try
        {
            var capture = new WasapiRecorderBuilder().WithDevice(device).WithLoopbackCapture()
                .WithBufferLength(30).WithMmcssThreadPriority("Audio").Build();
            recorder = capture;
            var format = capture.WaveFormat;
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
            if (format is WaveFormatExtensible ext) isFloat = ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
            if ((!isFloat && format.BitsPerSample is not (16 or 24 or 32)) || (isFloat && format.BitsPerSample != 32))
                throw new NotSupportedException("播放设备音频格式不支持，请使用 16/24/32 位 PCM 或 32 位浮点。");
            Timeline = new AudioTimeline(format.SampleRate);
            var timeline = Timeline;
            lock (healthSync) { packets = 0; lastPacketAt = lastSoundAt = null; rms = 0; }
            capture.DataAvailable += (bytes, flags, position, qpc) =>
            {
                try
                {
                    var audio = flags.HasFlag(AudioClientBufferFlags.Silent)
                        ? new float[bytes.Length / format.BlockAlign]
                        : WaveAudio.Decode(bytes, format.Channels, format.BitsPerSample, isFloat);
                    var start = flags.HasFlag(AudioClientBufferFlags.TimestampError) || qpc <= 0
                        ? NativeInput.Now - (double)audio.Length / format.SampleRate : qpc / 10000000.0;
                    timeline.Append(audio, start);
                    lock (healthSync)
                    {
                        packets++; lastPacketAt = NativeInput.Now; rms = AudioFeatures.Rms(audio);
                        if (rms >= .00008) lastSoundAt = lastPacketAt;
                    }
                }
                catch (Exception ex) { Failed?.Invoke(ex.Message); }
            };
            capture.RecordingStopped += (_, e) => { if (e.Exception is not null) Failed?.Invoke(e.Exception.Message); };
            capture.StartRecording();
            DeviceId = device.ID; DeviceName = device.FriendlyName;
        }
        catch { recorder?.Dispose(); recorder = null; device.Dispose(); device = null; throw; }
    }
    public async Task Stop()
    {
        var previous = recorder; recorder = null;
        if (previous is not null) { previous.StopRecording(); await previous.DisposeAsync(); }
        device?.Dispose(); device = null; Timeline.Clear(); DeviceId = "";
    }
    public async ValueTask DisposeAsync() { await Stop(); enumerator.Dispose(); }
}
