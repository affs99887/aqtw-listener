namespace Listener.App;

// Settings, the display filter, the personal library, learning drafts and diagnostics live in one
// per-user folder that every copy of the program shares, so a newly downloaded or extracted
// version starts with the user's own configuration. AQTW_DATA_DIR overrides the location; window
// checks (--ui-smoke) use a throwaway folder beside the program and never touch the user's data.
internal static class UserData
{
    public const string FolderName = "AqtwListener";
    public static string Root { get; private set; } = DefaultRoot();
    // Where versions up to 0.5.2.1 kept everything: local-data next to the program.
    public static string LegacyRoot => Path.Combine(AppContext.BaseDirectory, "local-data");
    public static string File(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string DefaultRoot() => Environment.GetEnvironmentVariable("AQTW_DATA_DIR") is { Length: > 0 } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    public static void UseIsolated(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path); Root = path;
    }

    // The first start with an empty shared folder copies the program folder's local-data into it.
    // Nothing is moved or deleted, so an older copy of the program keeps working with its own data.
    public static MigrationResult MigrateLegacy()
    {
        Directory.CreateDirectory(Root);
        var marker = File("migrated.json");
        if (System.IO.File.Exists(marker) || !Directory.Exists(LegacyRoot) ||
            string.Equals(Path.GetFullPath(LegacyRoot).TrimEnd('\\'), Path.GetFullPath(Root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return MigrationResult.None;
        if (Directory.EnumerateFileSystemEntries(Root).Any(entry => !Path.GetFileName(entry).Equals("display.json", StringComparison.OrdinalIgnoreCase)))
            return MigrationResult.None;
        var copied = 0;
        foreach (var source in Directory.EnumerateFiles(LegacyRoot, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Root, Path.GetRelativePath(LegacyRoot, source));
            if (System.IO.File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            System.IO.File.Copy(source, target); copied++;
        }
        JsonFile.Write(marker, new { from = LegacyRoot, files = copied, at = DateTimeOffset.Now });
        return new(true, LegacyRoot, copied);
    }
}

internal sealed record MigrationResult(bool Migrated, string? From, int Files)
{
    public static readonly MigrationResult None = new(false, null, 0);
}
