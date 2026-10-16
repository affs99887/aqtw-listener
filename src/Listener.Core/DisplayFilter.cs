namespace Listener.Core;

// Which catalogue items the overlay shows. It stores the hidden ids, so every item is shown by
// default and items a later version adds appear until the user hides them. Hidden items still
// count in a grid size's 大红概率; they only leave the cards.
public sealed class DisplayFilter
{
    public int SchemaVersion { get; set; } = 1;
    public List<string> HiddenItemIds { get; set; } = [];
    private HashSet<string>? hidden;
    private HashSet<string> Hidden => hidden ??= [.. HiddenItemIds];
    public event Action? Changed;

    public bool IsShown(ItemDefinition item) => !Hidden.Contains(item.Id);
    public bool IsShown(string itemId) => !Hidden.Contains(itemId);
    [System.Text.Json.Serialization.JsonIgnore] public int HiddenCount => Hidden.Count;

    public void Set(IEnumerable<string> itemIds, bool shown)
    {
        var changed = false;
        foreach (var id in itemIds) changed |= shown ? Hidden.Remove(id) : Hidden.Add(id);
        if (!changed) return;
        HiddenItemIds = [.. Hidden.Order(StringComparer.Ordinal)];
        Changed?.Invoke();
    }
    public void ShowOnly(IEnumerable<string> allItemIds, Func<string, bool> keep)
    {
        var ids = allItemIds.ToArray();
        Set(ids.Where(keep), true); Set(ids.Where(id => !keep(id)), false);
    }

    public static DisplayFilter Load(string path)
    {
        try { return File.Exists(path) ? JsonFile.Read<DisplayFilter>(path) : new(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(string path) => JsonFile.Write(path, this);
}
