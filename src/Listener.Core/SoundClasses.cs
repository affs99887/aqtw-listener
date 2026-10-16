namespace Listener.Core;

// One sound the game plays for several items: the pickup groups joined whenever they share an item
// or a reference recording. Items run 红, 金, 紫, 蓝, then larger first; classes run largest first.
public sealed record SoundClass(string Id, string Name, IReadOnlyList<ItemDefinition> Items, IReadOnlyList<string> GroupIds);

public static class SoundClasses
{
    public static IReadOnlyList<SoundClass> Build(SoundLibrary library, ReferenceAudio? references = null)
    {
        var groups = library.Groups.Where(group => group.Action == "pickup").ToList();
        var parent = groups.ToDictionary(group => group.Id, group => group.Id);
        string Find(string id) { while (parent[id] != id) id = parent[id] = parent[parent[id]]; return id; }
        void Join(string a, string b) { var (x, y) = (Find(a), Find(b)); if (x != y) parent[string.CompareOrdinal(x, y) < 0 ? y : x] = string.CompareOrdinal(x, y) < 0 ? x : y; }
        foreach (var shared in groups.SelectMany(group => group.ItemIds.Select(item => (item, group.Id))).GroupBy(entry => entry.item))
            foreach (var entry in shared.Skip(1)) Join(shared.First().Id, entry.Id);
        if (references is not null)
            foreach (var recording in references.Samples.Where(sample => parent.ContainsKey(sample.GroupId)).GroupBy(sample => sample.File))
                foreach (var sample in recording.Skip(1)) Join(recording.First().GroupId, sample.GroupId);

        var items = library.Items.ToDictionary(item => item.Id);
        var classes = groups.GroupBy(group => Find(group.Id)).Select(members =>
        {
            var classItems = members.SelectMany(group => group.ItemIds).Distinct().Where(items.ContainsKey).Select(id => items[id])
                .OrderBy(item => item.RarityRank).ThenByDescending(item => item.Cells).ThenBy(item => item.Name, StringComparer.Ordinal).ToArray();
            return new SoundClass(members.Key, Name(members, classItems), classItems, members.Select(group => group.Id).ToArray());
        }).Where(sound => sound.Items.Count > 0).ToList();
        // Items no pickup group lists (a personal library in progress) still need a place on the page.
        var listed = classes.SelectMany(sound => sound.Items).Select(item => item.Id).ToHashSet();
        var loose = library.Items.Where(item => !listed.Contains(item.Id)).OrderBy(item => item.RarityRank).ToArray();
        if (loose.Length > 0) classes.Add(new("unassigned", "暂无音效的物品", loose, []));
        return classes.OrderByDescending(sound => sound.Items.Count).ThenBy(sound => sound.Name, StringComparer.Ordinal).ToArray();
    }

    // A group already named after its class ("电脑硬件类（…）") names it; otherwise the first item does.
    private static string Name(IEnumerable<SoundGroup> groups, IReadOnlyList<ItemDefinition> items)
    {
        var named = groups.Select(group => group.Name.Split('（', '(')[0].Trim()).FirstOrDefault(name => name.EndsWith('类'));
        if (named is not null) return named;
        return items.Count == 1 ? items[0].Name : items[0].Name + "类";
    }
}
