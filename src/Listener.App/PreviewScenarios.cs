using Listener.Core;

namespace Listener.App;

// UI checks describe a layout, independently of a particular sound-library group.
internal static class PreviewScenarios
{
    internal static Candidate[] Thirteen(SoundLibrary library, double score = .99)
    {
        var sizes = new[] { (Cells: 2, Count: 1), (Cells: 3, Count: 1), (Cells: 4, Count: 6), (Cells: 6, Count: 5) };
        return sizes.SelectMany(size => library.Items.Where(item => item.Cells == size.Cells).Take(size.Count))
            .Select(item => new Candidate(item, score, library.Groups.First(group => group.ItemIds.Contains(item.Id)).Id))
            .ToArray();
    }
}
