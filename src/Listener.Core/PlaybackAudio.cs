namespace Listener.Core;

// What 听样本 plays. Matching references are processed (the bundled peer
// references are normalised to 0 dBFS and gated to silence), so they sound unlike
// the game. Playback clips are original captures: copied byte for byte and played
// as a time window, never filtered, normalised or resampled. A group without one
// has nothing to audition.
public sealed record PlaybackClip(string Id, string Label, string Action, string File, string Sha256,
    double StartSeconds, double? EndSeconds, string[] GroupIds, string Source = "");

public sealed class PlaybackAudio
{
    public const string ManifestName = "playback.json";
    public List<PlaybackClip> Clips { get; set; } = [];
    public static bool Exists(string root) => File.Exists(Path.Combine(root, ManifestName));
    public static PlaybackAudio Load(string root) => Exists(root) ? JsonFile.Read<PlaybackAudio>(Path.Combine(root, ManifestName)) : new();
    public static string VerifiedPath(string root, PlaybackClip clip)
    {
        var path = LibraryBuilder.ResolveFile(root, clip.File);
        if (!File.Exists(path) || !ReferenceAudio.Hash(path).Equals(clip.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("原声样本缺失或校验失败，请重新导入完整音效库。");
        return path;
    }
    public static AudioClip Read(string root, PlaybackClip clip)
    {
        var audio = WaveAudio.Read(VerifiedPath(root, clip));
        var first = (int)Math.Clamp(Math.Round(clip.StartSeconds * audio.SampleRate), 0, audio.Samples.Length);
        var last = clip.EndSeconds is { } end ? (int)Math.Clamp(Math.Round(end * audio.SampleRate), first, audio.Samples.Length) : audio.Samples.Length;
        return new(audio.Samples[first..last], audio.SampleRate);
    }
}
