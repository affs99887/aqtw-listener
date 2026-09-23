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
    string DeviceName { get; }
    uint TargetProcessId { get; }
    bool Running { get; }
    event Action<string>? Failed;
    AudioCaptureHealth Health();
    Task Start(uint processId);
    Task Stop();
}

internal sealed class LoopbackAudio : IPlaybackCapture
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private WasapiRecorder? recorder;
    public AudioTimeline Timeline { get; private set; } = new();
    public string DeviceName { get; private set; } = "";
    public uint TargetProcessId { get; private set; }
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
    public async Task Start(uint processId)
    {
        await lifecycle.WaitAsync();
        try { await StartLocked(processId); }
        finally { lifecycle.Release(); }
    }
    private async Task StartLocked(uint processId)
    {
        if (Running) throw new InvalidOperationException("采音已启动。");
        if (processId == 0) throw new ArgumentException("未找到游戏进程，无法只采集游戏声音。", nameof(processId));
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            throw new NotSupportedException("当前 Windows 版本不支持只采集游戏进程声音（需要 build 20348 或更新）。不会改用系统混音。");
        WasapiRecorder? capture = null;
        try
        {
            // The virtual process-loopback endpoint is independent of the playback device.
            // Never fall back to endpoint loopback: that would include voice-chat apps.
            capture = await Task.Run(() => BuildProcessRecorder(processId));
            var format = capture.WaveFormat;
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
            if (format is WaveFormatExtensible ext) isFloat = ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
            if ((!isFloat && format.BitsPerSample is not (16 or 24 or 32)) || (isFloat && format.BitsPerSample != 32))
                throw new NotSupportedException("游戏进程音频格式不支持，请使用 16/24/32 位 PCM 或 32 位浮点。");
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
            recorder = capture; TargetProcessId = processId; DeviceName = "UAGame 进程声音";
        }
        catch
        {
            if (capture is not null) await capture.DisposeAsync();
            recorder = null; TargetProcessId = 0; DeviceName = "";
            throw;
        }
    }
    private static Task<WasapiRecorder> BuildProcessRecorder(uint processId)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            throw new NotSupportedException("当前 Windows 版本不支持只采集游戏进程声音。");
        return new WasapiRecorderBuilder()
            .WithProcessLoopback(processId, ProcessLoopbackMode.IncludeTargetProcessTree)
            .WithBufferLength(30).WithMmcssThreadPriority("Audio").BuildAsync();
    }
    public async Task Stop()
    {
        await lifecycle.WaitAsync();
        try
        {
            var previous = recorder; recorder = null;
            if (previous is not null) { previous.StopRecording(); await previous.DisposeAsync(); }
            Timeline.Clear(); TargetProcessId = 0; DeviceName = "";
        }
        finally { lifecycle.Release(); }
    }
    public async ValueTask DisposeAsync() => await Stop();
}
