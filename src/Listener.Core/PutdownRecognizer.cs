namespace Listener.Core;

public sealed record PutdownMatch(string GroupId, double Score)
{
    public bool IsAuxiliary(RecognitionResult pickup) => pickup.Status != RecognitionStatus.Matched ||
        Score >= (pickup.BestMatch?.Score ?? 0) + .05;
}

public interface IPutdownRecognizer : IDisposable
{
    PutdownMatch? Match(float[] samples, int sampleRate, CancellationToken cancellation = default);
}

// Explicitly labelled putdown references live in a separate index. They cannot
// supply candidates or silently become pickup templates in personal libraries.
public sealed class PutdownRecognizer : IPutdownRecognizer
{
    private readonly RadarRecognizer recognizer;
    public PutdownRecognizer(string root, string engine)
    {
        var library = JsonFile.Read<SoundLibrary>(Path.Combine(root, "library.json"));
        if (library.Groups.Count == 0 || library.Groups.Any(group => group.Action != "putdown"))
            throw new InvalidDataException("辅助库只能包含明确标记的放下音效。");
        recognizer = new(library, Path.Combine(root, "radar-index.bin"), engine, "putdown");
    }
    public static PutdownRecognizer? TryLoad(string root, string engine)
    {
        if (!File.Exists(Path.Combine(root, "library.json"))) return null;
        try { return new(root, engine); }
        // Optional confirmation material must never prevent pickup recognition.
        catch (IOException) { return null; }
        catch (InvalidDataException) { return null; }
        catch (System.Text.Json.JsonException) { return null; }
    }
    public PutdownMatch? Match(float[] samples, int sampleRate, CancellationToken cancellation = default)
    {
        if (AudioFeatures.Rms(samples) < .00008) return null;
        var scores = recognizer.ScoreAudio(samples, sampleRate, cancellation).OrderByDescending(score => score.Score).ToArray();
        if (scores.Length == 0 || scores[0].Score < scores[0].Group.Threshold ||
            scores[0].Score - scores.Skip(1).Select(score => score.Score).DefaultIfEmpty(0).Max() < .05) return null;
        return new(scores[0].Group.Id, scores[0].Score);
    }
    public void Dispose() => recognizer.Dispose();
}
