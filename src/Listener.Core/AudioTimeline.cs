namespace Listener.Core;

// 按单调时钟放置样本；WASAPI 静音时不回调，空洞必须补零，不能复用上一件货的声音。
public sealed class AudioTimeline
{
    private readonly object sync = new();
    private readonly float[] buffer;
    private readonly long[] stamps;
    public int SampleRate { get; }
    public AudioTimeline(int sampleRate = 24000, int seconds = 5)
    {
        SampleRate = sampleRate; buffer = new float[sampleRate * seconds];
        stamps = Enumerable.Repeat(long.MinValue, buffer.Length).ToArray();
    }
    public void Clear()
    {
        lock (sync) { Array.Clear(buffer); Array.Fill(stamps, long.MinValue); }
    }
    public void Append(float[] samples, double startSeconds)
    {
        var begin = (long)Math.Round(startSeconds * SampleRate);
        lock (sync)
        {
            for (var i = Math.Max(0, samples.Length - buffer.Length); i < samples.Length; i++)
            {
                var frame = begin + i; var pos = (int)((frame % buffer.Length + buffer.Length) % buffer.Length);
                buffer[pos] = samples[i]; stamps[pos] = frame;
            }
        }
    }
    public float[] Slice(double startSeconds, double endSeconds)
    {
        var start = (long)Math.Round(startSeconds * SampleRate);
        var length = (int)Math.Clamp(Math.Round((endSeconds - startSeconds) * SampleRate), 0, buffer.Length);
        var output = new float[length];
        lock (sync)
        {
            for (var i = 0; i < length; i++)
            {
                var frame = start + i; var pos = (int)((frame % buffer.Length + buffer.Length) % buffer.Length);
                if (stamps[pos] == frame) output[i] = buffer[pos];
            }
        }
        return output;
    }
}
