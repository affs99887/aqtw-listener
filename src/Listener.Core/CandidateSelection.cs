namespace Listener.Core;

// Sound scores describe a group, not an individual item. An exact label is
// reserved for a strong, clearly separated group containing one item.
public static class CandidateSelection
{
    public static Candidate[] Select(SoundLibrary library, IEnumerable<(SoundGroup Group, double Score)> scores)
    {
        var ranked = scores.Where(entry => entry.Group.Action == "pickup" && entry.Group.Templates.Count > 0)
            .OrderByDescending(entry => entry.Score).ToArray();
        if (ranked.Length == 0 || ranked[0].Score < ranked[0].Group.Threshold) return [];
        var best = ranked[0];
        var next = ranked.Skip(1).Select(entry => entry.Score).DefaultIfEmpty(0).Max();
        var exact = best.Group.ItemIds.Length == 1 && best.Score >= Math.Max(.88, best.Group.Threshold + .1)
            && best.Score - next >= .07;
        var items = library.Items.ToDictionary(item => item.Id);
        return ranked.Where(entry => entry.Score >= entry.Group.Threshold ||
                entry.Score >= entry.Group.Threshold - .04 && best.Score - entry.Score <= .08)
            .SelectMany(entry => entry.Group.ItemIds.Select(id => new Candidate(items[id], entry.Score, entry.Group.Id,
                exact && entry.Group.Id == best.Group.Id ? RecognitionTag.Exact : RecognitionTag.Suspected)))
            .GroupBy(candidate => candidate.Item.Id)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Item.Id).ToArray();
    }
}
