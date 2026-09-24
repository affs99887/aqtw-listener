using System.Text.Json;
using System.Text.Json.Serialization;

namespace Listener.Core;

public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"文件为空：{path}");
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, true);
    }
}

public sealed record ItemDefinition(string Id, string Name, bool IsGold, decimal? ReferenceValue = null,
    string? ValueSource = null, string? ValueDate = null, int GridWidth = 1, int GridHeight = 1,
    string? Thumbnail = null, bool GridVerified = true, string? CatalogSource = null)
{
    public int Cells => GridVerified ? GridWidth * GridHeight : 0;
    [JsonIgnore] public string GridLabel => GridVerified ? $"{GridWidth}×{GridHeight}" : "格数待核实";
}

public sealed class SoundGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Action { get; set; } = "pickup";
    public string[] ItemIds { get; set; } = [];
    public double Threshold { get; set; } = 0.78;
    public List<SoundTemplate> Templates { get; set; } = [];
}

public sealed class SoundTemplate
{
    public string Id { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string RecordingId { get; set; } = "";
    public string AudioSha256 { get; set; } = "";
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public float[][] Features { get; set; } = [];
}

public sealed class SoundLibrary
{
    public int SchemaVersion { get; set; } = 1;
    public string FeatureVersion { get; set; } = AudioFeatures.Version;
    public string Version { get; set; } = "1.0.0";
    public string GameVersion { get; set; } = "S7";
    public string ValidationStatus { get; set; } = "uncalibrated";
    public string PrimaryEngine { get; set; } = RecognizerFactory.Engine;
    public string EngineIndexSha256 { get; set; } = "";
    public string Notes { get; set; } = "";
    public string[] CalibrationRecordingIds { get; set; } = [];
    public string[] CalibrationAudioHashes { get; set; } = [];
    public List<ItemDefinition> Items { get; set; } = [];
    public List<SoundGroup> Groups { get; set; } = [];

    public void Validate()
    {
        if (SchemaVersion != 1 || FeatureVersion != AudioFeatures.Version)
            throw new InvalidDataException("音效库格式不兼容，请更新程序或重新建立音效库。");
        if (Items.Select(x => x.Id).Distinct().Count() != Items.Count)
            throw new InvalidDataException("物品编号重复。");
        if (Groups.Select(x => x.Id).Distinct().Count() != Groups.Count)
            throw new InvalidDataException("音效组编号重复。");
        var ids = Items.Select(x => x.Id).ToHashSet();
        if (Items.Any(i => string.IsNullOrWhiteSpace(i.Id) || string.IsNullOrWhiteSpace(i.Name) || i.ReferenceValue < 0
            || i.GridWidth is < 1 or > 10 || i.GridHeight is < 1 or > 10
            || (i.Thumbnail is not null && (Path.IsPathRooted(i.Thumbnail) || i.Thumbnail.Contains("..")))))
            throw new InvalidDataException("物品资料不完整。");
        foreach (var group in Groups)
        {
            if (group.Threshold is < 0.1 or > 1 || !double.IsFinite(group.Threshold) || group.ItemIds.Length == 0
                || group.ItemIds.Any(id => !ids.Contains(id)) || group.ItemIds.Distinct().Count() != group.ItemIds.Length)
                throw new InvalidDataException($"音效组 {group.Name} 的门限或物品关联无效。");
            foreach (var t in group.Templates)
                if (t.Features.Length is < 3 or > 500 || t.Features.Any(f => f.Length != AudioFeatures.Bands || f.Any(v => !float.IsFinite(v))))
                    throw new InvalidDataException($"音效样本 {t.Id} 的特征无效。");
        }
    }
}

public enum RecognitionStatus { Listening, Analyzing, Matched, NoSound, Unknown, Interference, LibraryEmpty, Error }
public enum RecognitionTag { Suspected, Exact }
public sealed record Candidate(ItemDefinition Item, double Score, string GroupId, RecognitionTag Tag = RecognitionTag.Suspected);
public sealed record RecognitionResult(long OperationId, RecognitionStatus Status, bool IsFinal,
    IReadOnlyList<Candidate> Candidates, double ElapsedMilliseconds, string Message)
{
    public int CandidateCount => Candidates.Count;
    public int GoldCount => Candidates.Count(c => c.Item.IsGold);
    public double? GoldCandidateRatio => CandidateCount == 0 ? null : (double)GoldCount / CandidateCount;
    public Candidate? BestMatch => Candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Item.Id).FirstOrDefault();
    public Candidate? HighestValue => Candidates.Where(c => c.Item.ReferenceValue.HasValue)
        .OrderByDescending(c => c.Item.ReferenceValue).ThenByDescending(c => c.Score).FirstOrDefault();
}

// 用递增编号丢弃过时任务，包括已经进入 UI 消息队列的结果。
public sealed class OperationEpoch
{
    private long current;
    public long Next() => Interlocked.Increment(ref current);
    public bool IsCurrent(long id) => Interlocked.Read(ref current) == id;
}
