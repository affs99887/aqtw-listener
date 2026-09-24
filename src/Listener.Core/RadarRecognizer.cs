using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Listener.Core;

// A persistent, hidden worker reuses SoundRadar's unmodified DSP and quantized index.
// Requests contain in-memory PCM only. No files, network sockets, or process-per-click startup.
public sealed class RadarRecognizer : IRecognizer
{
    private readonly SoundLibrary library;
    private readonly Process worker;
    private readonly object sync = new();
    private long requestId;
    private bool disposed;
    public RadarRecognizer(SoundLibrary library, string indexPath, string executable)
    {
        library.Validate(); this.library = library;
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(indexPath)));
        if (string.IsNullOrWhiteSpace(library.EngineIndexSha256) || !hash.Equals(library.EngineIndexSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("声纹索引与物品目录不属于同一音效包，请整体替换 library 文件夹。");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("serve"); start.ArgumentList.Add(indexPath);
        worker = Process.Start(start) ?? throw new IOException("无法启动本地识别引擎。");
        worker.ErrorDataReceived += (_, _) => { }; worker.BeginErrorReadLine();
    }
    public (SoundGroup Group, double Score)[] ScoreAudio(float[] samples, int sampleRate, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (samples.Length > sampleRate * 5) throw new ArgumentException("请将单次音频裁剪至 5 秒以内。");
        var pcm = WaveAudio.Resample(samples, sampleRate, 48000);
        var encoded = Convert.ToBase64String(MemoryMarshal.AsBytes(pcm.AsSpan()));
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this); cancellation.ThrowIfCancellationRequested();
            if (worker.HasExited) throw new IOException("识别引擎已退出，请重启助手。");
            var id = ++requestId;
            worker.StandardInput.WriteLine(JsonSerializer.Serialize(new { id, pcm = encoded })); worker.StandardInput.Flush();
            // Finish reading one response even if superseded, preserving protocol boundaries.
            string? line;
            try { line = worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
            catch (TimeoutException)
            {
                if (!worker.HasExited) worker.Kill();
                throw new IOException("识别引擎响应超时，请重启助手。");
            }
            if (line is null) throw new IOException("识别引擎断开连接。");
            var response = JsonSerializer.Deserialize<EngineResponse>(line, JsonFile.Options) ?? throw new IOException("识别引擎响应为空。");
            cancellation.ThrowIfCancellationRequested();
            if (response.Id != id || response.Error is not null) throw new IOException(response.Error ?? "识别引擎操作编号不一致。");
            return library.Groups.Where(g => g.Action == "pickup" && g.Templates.Count > 0)
                .Select(g => (g, response.Scores.GetValueOrDefault(g.Id))).ToArray();
        }
    }
    public RecognitionResult Recognize(float[] samples, int sampleRate, long operationId = 0, bool final = true, CancellationToken cancellation = default)
        => Analyze(samples, sampleRate, operationId, final, cancellation).Result;
    public RecognitionAnalysis Analyze(float[] samples, int sampleRate, long operationId = 0, bool final = true, CancellationToken cancellation = default)
    {
        var watch = Stopwatch.StartNew();
        GroupScore[] ranked = [];
        RecognitionAnalysis Empty(RecognitionStatus s, string m) => new(new(operationId, s, final, [], watch.Elapsed.TotalMilliseconds, m), ranked);
        if (library.Groups.All(g => g.Action != "pickup" || g.Templates.Count == 0)) return Empty(RecognitionStatus.LibraryEmpty, "音效库没有拾起样本");
        if (samples.Length < sampleRate * .03 || AudioFeatures.Rms(samples) < .00008) return Empty(RecognitionStatus.NoSound, "没有听到有效声音");
        if (samples.Count(v => Math.Abs(v) > .995) > samples.Length * .05) return Empty(RecognitionStatus.Interference, "声音削波严重，请降低音量");
        var scores = ScoreAudio(samples, sampleRate, cancellation);
        ranked = scores.OrderByDescending(s => s.Score).Select(s => new GroupScore(s.Group.Id, s.Score)).ToArray();
        var candidates = CandidateSelection.Select(library, scores);
        return candidates.Length == 0 ? Empty(RecognitionStatus.Unknown, "识别失败 · 未匹配到已收录音效")
            : new(new(operationId, RecognitionStatus.Matched, final, candidates, watch.Elapsed.TotalMilliseconds, final ? "识别完成" : "初步候选 · 正在继续听"), ranked);
    }
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return; disposed = true;
            try { worker.StandardInput.Close(); if (!worker.WaitForExit(1000)) worker.Kill(); } catch (InvalidOperationException) { }
            worker.Dispose();
        }
    }
    private sealed class EngineResponse
    {
        public long Id { get; set; }
        public Dictionary<string, double> Scores { get; set; } = [];
        public string? Error { get; set; }
    }
}

public static class RecognizerFactory
{
    public static IRecognizer Create(SoundLibrary library, string? libraryRoot = null) => library.PrimaryEngine switch
    {
        "dtw" => new Recognizer(library),
        "soundradar" => new RadarRecognizer(library, Path.Combine(libraryRoot ?? Path.Combine(AppContext.BaseDirectory, "library"), "radar-index.bin"),
            Path.Combine(AppContext.BaseDirectory, "engine", "Listener.Engine.exe")),
        _ => throw new InvalidDataException("未知识别引擎：" + library.PrimaryEngine)
    };
}
