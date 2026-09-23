namespace Listener.App;

internal sealed class HistoryWindow : Window
{
    private readonly RecognitionHistory history;
    private readonly ListBox records = new() { Background = Theme.Panel, Foreground = Theme.Text,
        BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly CandidatePanel details;
    private readonly TextBlock count = Theme.Label("", 12, Theme.Muted);
    private bool refreshing;
    internal IReadOnlyList<long> VisibleRecordIds => records.Items.Cast<ListBoxItem>().Select(row => ((RecognitionEntry)row.Tag).Id).ToArray();

    public HistoryWindow(RecognitionHistory history, string libraryRoot, Settings settings, Action<RecognitionEntry>? compare = null)
    {
        this.history = history;
        BrandAssets.StyleWindow(this);
        Title = "识别历史 · 本次运行"; Width = Math.Min(1060, SystemParameters.WorkArea.Width);
        WindowPlacement.UseContentHeight(this); ResizeMode = ResizeMode.CanMinimize;
        Background = Theme.Background; Foreground = Theme.Text; FontFamily = new FontFamily("Microsoft YaHei UI");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        details = new(libraryRoot, new Settings { ShowNames = true, ThumbnailSize = settings.ThumbnailSize, FontScale = settings.FontScale });
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(Theme.Label("识别历史", 24));
        header.Children.Add(Theme.Label("保留本次运行最近 100 条。相邻重复声音会合并；退出软件后清空。", 12, Theme.Muted));
        var actions = new DockPanel();
        if (compare is not null)
        {
            var button = Theme.Button("在浮窗中对比", (_, _) => { if ((records.SelectedItem as ListBoxItem)?.Tag is RecognitionEntry entry) compare(entry); });
            DockPanel.SetDock(button, Dock.Right); actions.Children.Add(button);
        }
        var clear = Theme.Button("清空历史", (_, _) => history.ClearHistory());
        DockPanel.SetDock(clear, Dock.Right); actions.Children.Add(clear); actions.Children.Add(count); header.Children.Add(actions);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        body.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(body);
        records.Margin = new Thickness(0, 0, 14, 0);
        ScrollViewer.SetHorizontalScrollBarVisibility(records, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(records, ScrollBarVisibility.Auto);
        ScrollViewer.SetCanContentScroll(records, true);
        body.Children.Add(records);
        Grid.SetColumn(details, 1); body.Children.Add(details);
        records.SelectionChanged += (_, _) => { if (!refreshing) ShowSelection(); };
        history.Changed += Refresh;
        Closed += (_, _) => history.Changed -= Refresh;
        Refresh();
    }
    private void Refresh()
    {
        var selected = (records.SelectedItem as ListBoxItem)?.Tag as RecognitionEntry;
        refreshing = true;
        records.Items.Clear(); ListBoxItem? restore = null;
        foreach (var entry in history.Entries)
        {
            var content = new StackPanel { Margin = new Thickness(8) };
            content.Children.Add(Theme.Label($"{entry.LastSeen:HH:mm:ss} · {entry.Source}", 13));
            content.Children.Add(Theme.Label($"{entry.Result.CandidateCount} 件候选 · 按格数查看大红概率" +
                (entry.Matches > 1 ? $" · 合并 {entry.Matches} 次" : ""), 11, Theme.Gold));
            content.Children.Add(Theme.Label(string.Join("、", entry.Result.Candidates.Take(3).Select(c => c.Item.Name)) +
                (entry.Result.CandidateCount > 3 ? "等" : ""), 11, Theme.Muted));
            foreach (var label in content.Children.OfType<TextBlock>())
            { label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; }
            var row = new ListBoxItem { Content = content, Tag = entry, Height = 86 }; records.Items.Add(row);
            if (entry.Id == selected?.Id) restore = row;
        }
        count.Text = $"{history.Entries.Count} / {RecognitionHistory.Capacity} 条 · 点击记录查看全部候选";
        if (restore is not null) records.SelectedItem = restore;
        else if (records.Items.Count > 0) records.SelectedIndex = 0;
        refreshing = false; ShowSelection();
    }
    private void ShowSelection()
    {
        var entry = (records.SelectedItem as ListBoxItem)?.Tag as RecognitionEntry;
        if (entry?.LibraryRoot is { Length: > 0 } root) details.SetLibraryRoot(root);
        details.SetState(entry is null ? "本次运行尚无历史记录" : $"历史记录 · {entry.LastSeen:yyyy-MM-dd HH:mm:ss}");
        details.ShowResult(entry is null
            ? new(0, RecognitionStatus.Unknown, true, [], 0, "识别成功后会保存在这里")
            : entry.Result with { Message = $"{entry.Source} · 完整候选" });
    }
}
