using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Listener.App;

internal sealed class AudioPreviewPlayer : IDisposable
{
    private WasapiPlayer? player;
    private MMDevice? device;
    private RawSourceWaveStream? source;
    public event Action<string>? Status;
    public void Play(AudioClip clip, string deviceId)
    {
        Stop();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            device = string.IsNullOrEmpty(deviceId) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(deviceId);
            var bytes = MemoryMarshal.AsBytes(clip.Samples.AsSpan()).ToArray();
            source = new RawSourceWaveStream(bytes, 0, bytes.Length, WaveFormat.CreateIeeeFloatWaveFormat(clip.SampleRate, 1));
            var current = new WasapiPlayerBuilder().WithDevice(device).WithSharedMode().WithLatency(80).Build(); player = current;
            current.PlaybackStopped += (_, e) => Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (!ReferenceEquals(player, current)) return;
                Stop(); Status?.Invoke(e.Exception is null ? "播放结束" : "播放失败 · " + e.Exception.Message);
            });
            current.Init(source); current.Play(); Status?.Invoke("正在播放 · 识别已暂停");
        }
        catch { Stop(); throw; }
    }
    public void Stop()
    {
        var previous = player; player = null;
        if (previous is not null) { previous.Stop(); previous.Dispose(); }
        source?.Dispose(); source = null; device?.Dispose(); device = null;
    }
    public void Dispose() => Stop();
}
