using System.Numerics;

namespace Listener.Core;

// Frames stored as SoundTemplate.Features (library schema v1) and used to reject
// recordings without a usable sound. Recognition itself is InMatchRecognizer's.
public static class AudioFeatures
{
    public const string Version = "logmel24k-48-v1";
    public const int Rate = 24000, Bands = 48, Frame = 600, Hop = 240, FftSize = 1024;
    private static readonly double[][] Filters = BuildFilters();
    private static readonly double[] Window = Enumerable.Range(0, Frame).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Frame - 1))).ToArray();

    public static double Rms(float[] samples) => samples.Length == 0 ? 0 : Math.Sqrt(samples.Sum(s => (double)s * s) / samples.Length);

    public static float[][] Extract(float[] input, int rate)
    {
        var data = WaveAudio.Resample(input, rate, Rate);
        if (data.Length < Frame || Rms(data) < 0.00003) return [];
        var energies = new List<double>();
        var frames = new List<float[]>();
        var spectrum = new Complex[FftSize];
        for (var start = 0; start + Frame <= data.Length; start += Hop)
        {
            Array.Clear(spectrum);
            double energy = 0;
            for (var i = 0; i < Frame; i++)
            {
                var v = data[start + i];
                energy += v * v;
                spectrum[i] = new Complex(v * Window[i], 0);
            }
            Fft(spectrum);
            var power = new double[FftSize / 2 + 1];
            for (var k = 0; k < power.Length; k++) power[k] = spectrum[k].Magnitude * spectrum[k].Magnitude;
            var feature = new float[Bands];
            for (var b = 0; b < Bands; b++)
            {
                double value = 0;
                for (var k = 0; k < power.Length; k++) value += power[k] * Filters[b][k];
                feature[b] = (float)Math.Log(Math.Max(1e-12, value));
            }
            energies.Add(energy / Frame); frames.Add(feature);
        }
        if (frames.Count < 3) return [];
        // 统一裁去低能量边缘；保留音效前后各 20 ms。
        var gate = energies.Max() * 0.015;
        var first = Math.Max(0, energies.FindIndex(e => e >= gate) - 2);
        var last = Math.Min(frames.Count - 1, energies.FindLastIndex(e => e >= gate) + 2);
        var result = frames.Skip(first).Take(last - first + 1).ToArray();
        foreach (var f in result)
        {
            // 频谱去均值并归一化，减小音量及录屏增益差异。
            var max = f.Max();
            for (var b = 0; b < Bands; b++) f[b] = Math.Max(f[b], max - 13.8f);
            var mean = f.Average();
            double norm = 0;
            for (var b = 0; b < Bands; b++) { f[b] -= mean; norm += f[b] * f[b]; }
            norm = Math.Sqrt(norm);
            if (norm > 1e-9) for (var b = 0; b < Bands; b++) f[b] /= (float)norm;
        }
        return result;
    }

    private static double[][] BuildFilters()
    {
        static double Mel(double hz) => 2595 * Math.Log10(1 + hz / 700);
        static double Hz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);
        var edges = Enumerable.Range(0, Bands + 2).Select(i => Hz(Mel(100) + i * (Mel(11000) - Mel(100)) / (Bands + 1))).ToArray();
        return Enumerable.Range(0, Bands).Select(b => Enumerable.Range(0, FftSize / 2 + 1).Select(k =>
        {
            var hz = (double)k * Rate / FftSize;
            return Math.Max(0, Math.Min((hz - edges[b]) / (edges[b + 1] - edges[b]), (edges[b + 2] - hz) / (edges[b + 2] - edges[b + 1])));
        }).ToArray()).ToArray();
    }

    internal static void Fft(Complex[] data)
    {
        var n = data.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / len);
            for (var i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (var j = 0; j < len / 2; j++)
                {
                    var even = data[i + j]; var odd = data[i + j + len / 2] * w;
                    data[i + j] = even + odd; data[i + j + len / 2] = even - odd; w *= step;
                }
            }
        }
    }
}
