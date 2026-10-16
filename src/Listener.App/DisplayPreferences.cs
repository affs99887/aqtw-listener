namespace Listener.App;

// The user's choice of items to show, shared by the overlay, the operation view and the history.
// Every change is written at once to the per-user folder, so all copies of the program agree.
internal static class DisplayPreferences
{
    public static string FilePath => UserData.File("display.json");
    private static DisplayFilter? filter;
    public static DisplayFilter Filter => filter ??= Open();
    public static event Action? Changed;
    public static string? LastSaveError { get; private set; }

    private static DisplayFilter Open()
    {
        var opened = DisplayFilter.Load(FilePath);
        opened.Changed += () =>
        {
            try { opened.Save(FilePath); LastSaveError = null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastSaveError = ex.Message; }
            Changed?.Invoke();
        };
        return opened;
    }

    // For checks that start from a clean slate in the same process.
    public static void Reload() { filter = null; Changed?.Invoke(); }
}
