using System.Buffers.Binary;

namespace Listener.Core;

public sealed record AudioClip(float[] Samples, int SampleRate);

public static class WaveAudio
{
    public static AudioClip Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var r = new BinaryReader(stream);
        if (new string(r.ReadChars(4)) != "RIFF") throw new InvalidDataException("仅支持 RIFF WAV。");
        r.ReadUInt32();
        if (new string(r.ReadChars(4)) != "WAVE") throw new InvalidDataException("无效 WAV。");
        ushort format = 0, channels = 0, bits = 0;
        int rate = 0;
        byte[]? data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            var name = new string(r.ReadChars(4));
            var size = r.ReadUInt32();
            var start = stream.Position;
            if (size > stream.Length - start) throw new InvalidDataException("WAV 数据损坏。");
            if (name == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("无效 WAV 格式块。");
                format = r.ReadUInt16(); channels = r.ReadUInt16(); rate = r.ReadInt32();
                r.ReadUInt32(); r.ReadUInt16(); bits = r.ReadUInt16();
                if (format == 0xfffe && size >= 40) { stream.Position = start + 24; format = r.ReadUInt16(); }
            }
            else if (name == "data")
            {
                if (size > 100_000_000) throw new InvalidDataException("请先裁剪 WAV 至短片段（上限 100 MB）。");
                data = r.ReadBytes((int)size);
            }
            stream.Position = start + size + (size & 1);
        }
        if (data is null || channels is < 1 or > 16 || rate is < 8000 or > 192000
            || !(format == 1 && bits is 16 or 24 or 32 || format == 3 && bits == 32))
            throw new InvalidDataException("支持 PCM16/24/32 或 float32 WAV，8–192 kHz。");
        return new AudioClip(Decode(data, channels, bits, format == 3), rate);
    }

    public static float[] Decode(ReadOnlySpan<byte> bytes, int channels, int bits, bool floating)
    {
        var stride = channels * bits / 8;
        if (stride <= 0 || bits is not (16 or 24 or 32)) throw new InvalidDataException("不支持的音频格式。");
        var result = new float[bytes.Length / stride];
        for (var i = 0; i < result.Length; i++)
        {
            double sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var b = bytes.Slice(i * stride + c * bits / 8);
                var v = floating ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b)) : bits switch
                {
                    16 => BinaryPrimitives.ReadInt16LittleEndian(b) / 32768f,
                    24 => ((b[0] | b[1] << 8 | b[2] << 16) << 8 >> 8) / 8388608f,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(b) / 2147483648f
                };
                sum += float.IsFinite(v) ? Math.Clamp(v, -1, 1) : 0;
            }
            result[i] = (float)(sum / channels);
        }
        return result;
    }

    public static void Write(string path, float[] samples, int rate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + samples.Length * 2); w.Write("WAVEfmt "u8);
        w.Write(16); w.Write((ushort)1); w.Write((ushort)1); w.Write(rate); w.Write(rate * 2);
        w.Write((ushort)2); w.Write((ushort)16); w.Write("data"u8); w.Write(samples.Length * 2);
        foreach (var v in samples) w.Write((short)(Math.Clamp(v, -1, 1) * short.MaxValue));
    }

    public static float[] Resample(float[] samples, int from, int to)
    {
        if (from == to) return samples;
        if (from <= 0 || to <= 0) throw new ArgumentOutOfRangeException(nameof(from));
        var output = new float[(int)((long)samples.Length * to / from)];
        // 窗函数 sinc 抗混叠，避免线性抽样把高频折叠为错误指纹。
        var cutoff = Math.Min(1.0, (double)to / from) * 0.94;
        const int radius = 16;
        for (var i = 0; i < output.Length; i++)
        {
            double p = (double)i * from / to, sum = 0, weights = 0;
            var center = (int)p;
            for (var j = Math.Max(0, center - radius); j <= Math.Min(samples.Length - 1, center + radius); j++)
            {
                var d = p - j;
                if (Math.Abs(d) > radius) continue;
                var x = Math.PI * cutoff * d;
                var weight = (Math.Abs(x) < 1e-9 ? 1 : Math.Sin(x) / x) * (0.5 + 0.5 * Math.Cos(Math.PI * d / radius));
                sum += samples[j] * weight; weights += weight;
            }
            output[i] = weights == 0 ? 0 : (float)(sum / weights);
        }
        return output;
    }
}
