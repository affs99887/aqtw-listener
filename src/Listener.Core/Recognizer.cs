namespace Listener.Core;

public interface IRecognizer : IDisposable
{
    RecognitionResult Recognize(float[] samples, int sampleRate, long operationId = 0, bool final = true, CancellationToken cancellation = default);
    RecognitionAnalysis Analyze(float[] samples, int sampleRate, long operationId = 0, bool final = true, CancellationToken cancellation = default)
        => new(Recognize(samples, sampleRate, operationId, final, cancellation), []);
    // Analyse a clip whose sound event starts onsetSeconds into the clip. Engines that
    // do not anchor on the onset simply analyse the whole clip.
    RecognitionAnalysis AnalyzeAt(float[] samples, int sampleRate, double onsetSeconds, long operationId = 0, bool final = true, CancellationToken cancellation = default)
        => Analyze(samples, sampleRate, operationId, final, cancellation);
    (SoundGroup Group, double Score)[] ScoreAudio(float[] samples, int sampleRate, CancellationToken cancellation = default);
}

public static class RecognizerFactory
{
    public const string Engine = "inmatch";
    public static IRecognizer Create(SoundLibrary library, string? libraryRoot = null) => library.PrimaryEngine == Engine
        ? new InMatchRecognizer(library, libraryRoot ?? Path.Combine(AppContext.BaseDirectory, "library"))
        : throw new InvalidDataException($"音效库使用已停用的识别引擎「{library.PrimaryEngine}」，请整体替换 library 文件夹，或回退个人库后重新建库。");
}
