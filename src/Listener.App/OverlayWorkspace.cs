using System.Globalization;
using Microsoft.Win32;

namespace Listener.App;

// A single, bounded workspace inside the overlay. Each editor is a short step, not a long scrolling form.
internal sealed class OverlayWorkspace : Border, IDisposable
{
    private readonly ListeningController controller;
    private readonly PersonalLibraryStore store;
    private readonly OverlayWindow overlay;
    private readonly Settings settings;
    private readonly Func<LibraryVersion, bool, CancellationToken, Task> activate;
    private readonly AudioPreviewPlayer player = new();
    private readonly GuidedLearning learning;
    private readonly ContentControl page = new();
    private readonly TextBlock notice = Theme.Label("", 11, Theme.Accent);
    private AnalysisSnapshot? selected;
    private LearningDraft? draft;
    private CancellationTokenSource? operation;
    private bool busy, disposed;
    public event Action? CaptureStarted;
    public event Action? CaptureFinished;
    public bool Learning => learning.Running;
    internal string StatusText => notice.Text;
    public OverlayWorkspace(ListeningController controller, PersonalLibraryStore store, OverlayWindow overlay, Settings settings,
        Func<LibraryVersion, bool, CancellationToken, Task> activate)
    {
        this.controller = controller; this.store = store; this.overlay = overlay; this.settings = settings; this.activate = activate;
        learning = new(controller, store); Background = Theme.Background; Padding = new Thickness(8);
        var body = new DockPanel(); Child = body;
        var nav = Row(Button("声音对比", ShowSound), Button("完整候选", ShowCandidates), Button("补库 / 学习", ShowLearning), Button("个人库", ShowPersonal));
        DockPanel.SetDock(nav, Dock.Top); body.Children.Add(nav);
        notice.FontSize = 11 * settings.FontScale; notice.TextWrapping = TextWrapping.NoWrap; notice.TextTrimming = TextTrimming.CharacterEllipsis; notice.Margin = new Thickness(0, 3, 0, 0);
        var foot = new DockPanel(); var detail = Button("详情", () => MessageBox.Show(overlay, notice.Text, "当前状态"));
        DockPanel.SetDock(detail, Dock.Right); foot.Children.Add(detail); foot.Children.Add(notice);
        DockPanel.SetDock(foot, Dock.Bottom); body.Children.Add(foot);
        body.Children.Add(new FitContent { Child = page });
        player.Status += Message;
        learning.Progress += text => { Message(text); page.Content = TextPage(draft?.Item.Name ?? "学习采样", text, "Ctrl+Alt+C 暂停并操作浮窗"); };
        ShowSound();
    }
    private Button Button(string text, Action action)
    {
        var button = OverlayWindow.SmallButton(text, () => { if (!busy) { try { action(); } catch (Exception ex) { Message(ex.Message); } } });
        button.FontSize = 11 * settings.FontScale; return button;
    }
    private static WrapPanel Row(params UIElement[] elements)
    { var row = new WrapPanel(); foreach (var element in elements) row.Children.Add(element); return row; }
    private StackPanel TextPage(params string[] lines)
    { var stack = new StackPanel(); foreach (var line in lines) stack.Children.Add(Theme.Label(line, 12 * settings.FontScale)); return stack; }
    private ComboBox Combo<T>(IEnumerable<T> values, string? display = null)
    { return new ComboBox { FontSize = 12 * settings.FontScale, ItemsSource = values.ToArray(), DisplayMemberPath = display ?? "", Margin = new Thickness(0, 2, 0, 3), MinHeight = 28 }; }
    private void Message(string text) { notice.Text = text; notice.ToolTip = text; }
    private async void Run(Func<Task> work)
    {
        if (busy || disposed) return;
        busy = true; operation?.Dispose(); operation = new();
        try { await work(); }
        catch (OperationCanceledException) { Message("操作已取消，原音效库保持可用"); }
        catch (Exception ex) { Message(ex.Message); }
        finally { busy = false; }
    }
    public void Select(AnalysisSnapshot? snapshot)
    { player.Stop(); selected = snapshot ?? controller.Audio.Recent.FirstOrDefault(s => s.Analysis.Result.IsFinal) ?? controller.Audio.Recent.FirstOrDefault(); controller.Audio.Pin(selected?.Id); ShowSound(); }
    public void OpenLearning() => ShowLearning();
    public void PauseLearning() { learning.Pause(); player.Stop(); }
    public void Stop()
    { operation?.Cancel(); learning.Pause(); player.Stop(); controller.Audio.Pin(null); }
    private void Play(AudioClip clip)
    {
        if (controller.Mode != AssistantMode.Interaction) throw new InvalidOperationException("先暂停采样，再试听声音");
        player.Play(clip, settings.DeviceId);
    }
    private void ShowSound()
    {
        var body = new StackPanel(); page.Content = body;
        var snapshots = controller.Audio.Recent.Concat(controller.History.Entries.Select(e => controller.Audio.Get(e.SnapshotId)).OfType<AnalysisSnapshot>())
            .Concat(selected is null ? [] : new[] { selected }).DistinctBy(s => s.Id).OrderByDescending(s => s.At).ToArray();
        selected ??= snapshots.FirstOrDefault(); controller.Audio.Pin(selected?.Id);
        var clips = Combo(snapshots); clips.SelectedItem = selected;
        clips.SelectionChanged += (_, _) => { selected = clips.SelectedItem as AnalysisSnapshot; controller.Audio.Pin(selected?.Id); player.Stop(); ShowSound(); };
        body.Children.Add(clips);
        body.Children.Add(Row(Button("播放片段", () => Play(selected?.Audio ?? throw new InvalidOperationException("片段已过期或尚未收到声音"))),
            Button("停止", () => { player.Stop(); Message("播放已停止"); }), Button("保存 WAV", SaveClip), Button("用于学习", ShowLearning),
            Button("上次匹配", () => Select(controller.Audio.Get(controller.History.Latest?.SnapshotId) ?? throw new InvalidOperationException("尚无上次匹配片段")))));
        var library = selected?.Library ?? controller.Library; var root = selected?.LibraryRoot ?? controller.LibraryRoot;
        var passed = selected?.Analysis.Result.Candidates.Select(c => c.GroupId).ToHashSet() ?? [];
        var choices = (selected?.Analysis.Scores ?? []).Where(s => passed.Contains(s.GroupId)).Select(s => new GroupChoice(s.GroupId,
            "已匹配 · " + library.Groups.Single(g => g.Id == s.GroupId).Name)).ToArray();
        var showNear = new CheckBox { Content = "展开相近音效（仅供对比，不计入候选占比）", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 3, 0, 3) };
        body.Children.Add(showNear);
        var groups = Combo(choices); body.Children.Add(groups);
        var variants = Combo(Array.Empty<ReferenceChoice>()); var listen = Button("听参考", () =>
        {
            var reference = (variants.SelectedItem as ReferenceChoice)?.Sample ?? throw new InvalidOperationException("此组没有可试听的参考文件");
            Play(WaveAudio.Read(ReferenceAudio.VerifiedPath(root, reference)));
        });
        var sampleRow = new DockPanel(); DockPanel.SetDock(listen, Dock.Right); sampleRow.Children.Add(listen); sampleRow.Children.Add(variants); body.Children.Add(sampleRow);
        groups.SelectionChanged += (_, _) =>
        {
            var group = (groups.SelectedItem as GroupChoice)?.Id;
            variants.ItemsSource = ReferenceAudio.Load(root).Samples.Where(s => s.GroupId == group).Select((s, i) => new ReferenceChoice(s, $"参考 {i + 1} · 同音组共享")).ToArray();
            variants.SelectedIndex = variants.Items.Count > 0 ? 0 : -1; listen.IsEnabled = variants.Items.Count > 0;
        };
        void RefreshGroups()
        {
            var near = showNear.IsChecked == true ? (selected?.Analysis.Scores ?? []).Where(s => !passed.Contains(s.GroupId))
                .OrderByDescending(s => s.Score).Take(3).Select(s => new GroupChoice(s.GroupId, "仅供对比 · " + library.Groups.Single(g => g.Id == s.GroupId).Name)) : [];
            groups.ItemsSource = choices.Concat(near).ToArray(); groups.SelectedIndex = groups.Items.Count > 0 ? 0 : -1;
        }
        showNear.Click += (_, _) => RefreshGroups(); RefreshGroups();
        Message(selected is null ? "尚无声音片段；可先进入补库选择物品" : $"{selected.At:HH:mm:ss} · {selected.Analysis.Result.Message} · {selected.Library.Version}");
    }
    private void SaveClip()
    {
        var audio = selected?.Audio ?? throw new InvalidOperationException("片段已过期或尚未收到声音");
        var dialog = new SaveFileDialog { Filter = "WAV 音频|*.wav", FileName = $"声音-{selected!.At:yyyyMMdd-HHmmss}.wav" };
        if (dialog.ShowDialog(overlay) == true) { WaveAudio.Write(dialog.FileName, audio.Samples, audio.SampleRate); Message("声音已保存"); }
    }
    private void ShowCandidates()
    {
        var panel = new CandidatePanel(selected?.LibraryRoot ?? controller.LibraryRoot, settings, interactive: true, compact: true, minimal: true);
        panel.ShowResult(selected?.Analysis.Result); panel.RefreshAppearance(); page.Content = panel;
        Message(selected is null ? "尚无选中片段" : $"{selected.At:HH:mm:ss} · {selected.Analysis.Result.CandidateCount} 件候选 · 大金占比 {selected.Analysis.Result.GoldCandidateRatio:P0}（非概率）");
    }
    private void ShowLearning()
    {
        var body = TextPage("先确认实际拖动的物品，再录制或添加样本"); page.Content = body;
        var items = Combo(controller.Library.Items.OrderBy(i => i.Name), nameof(ItemDefinition.Name)); body.Children.Add(items);
        body.Children.Add(Row(Button("选择此物品", () =>
        {
            var item = items.SelectedItem as ItemDefinition ?? throw new InvalidOperationException("请选择物品");
            var groups = controller.Library.Groups.Where(g => g.ItemIds.Contains(item.Id)).ToArray();
            if (groups.Length != 1) { ChooseTargetGroup(item, groups); return; }
            CreateDraft(item, groups[0].Id, store.Profile().Items.Any(i => i.Item.Id == item.Id));
        }), Button("录入新物品", NewItem), Button("继续草稿", ShowDraftPicker)));
    }
    private void ChooseTargetGroup(ItemDefinition item, SoundGroup[] groups)
    {
        var body = TextPage("请选择要补充的音效组"); page.Content = body;
        var choice = Combo(groups, nameof(SoundGroup.Name)); body.Children.Add(choice);
        body.Children.Add(Button("确认", () => CreateDraft(item, (choice.SelectedItem as SoundGroup)?.Id ?? throw new InvalidOperationException("请选择音效组"), false)));
    }
    private void CreateDraft(ItemDefinition item, string groupId, bool newItem)
    { draft = new() { Item = item, GroupId = groupId, IsNewItem = newItem }; store.SaveDraft(draft); ShowDraft(); }
    private void NewItem()
    {
        var body = TextPage("新物品 · 第 1 / 2 步"); page.Content = body;
        var name = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 2, 0, 4) };
        var rarity = Combo(new[] { "大金", "非大金" }); body.Children.Add(name); body.Children.Add(rarity);
        name.ToolTip = "物品名称";
        body.Children.Add(Button("下一步：格数和图片", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || rarity.SelectedIndex < 0) throw new InvalidOperationException("请填写名称并选择分类");
            NewItemDetails(name.Text.Trim(), rarity.SelectedIndex == 0);
        }));
    }
    private void NewItemDetails(string name, bool gold)
    {
        var body = TextPage("新物品 · 第 2 / 2 步：宽 × 高（格）"); page.Content = body;
        var width = new TextBox { Text = "1", Width = 55, Padding = new Thickness(6) };
        var height = new TextBox { Text = "1", Width = 55, Padding = new Thickness(6) };
        var price = new TextBox { Width = 110, Padding = new Thickness(6), ToolTip = "回收参考价，可留空" };
        body.Children.Add(Row(width, Theme.Label(" × "), height, Theme.Label(" 参考价 ", 11), price));
        string? image = null;
        body.Children.Add(Row(Button("选择图片（可选）", () =>
        {
            var dialog = new OpenFileDialog { Filter = "物品图片|*.png;*.jpg;*.jpeg" };
            if (dialog.ShowDialog(overlay) == true)
            {
                using var stream = File.OpenRead(dialog.FileName);
                _ = System.Windows.Media.Imaging.BitmapFrame.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                image = store.ImportImage(dialog.FileName); Message("图片已保存");
            }
        }), Button("建立草稿", () =>
        {
            if (!int.TryParse(width.Text, out var w) || !int.TryParse(height.Text, out var h) || w is < 1 or > 10 || h is < 1 or > 10)
                throw new InvalidOperationException("格数宽高应为 1–10");
            decimal? value = string.IsNullOrWhiteSpace(price.Text) ? null : decimal.TryParse(price.Text, out var v) && v >= 0 ? v : throw new InvalidOperationException("参考价应为非负数字或留空");
            var id = "user-" + Guid.NewGuid().ToString("N");
            CreateDraft(new(id, name, gold, value, value.HasValue ? "用户录入，待核实" : null, Thumbnail: image, GridWidth: w, GridHeight: h), id, true);
        })));
    }
    private void ShowDraftPicker()
    {
        var body = TextPage("本地学习草稿"); page.Content = body;
        var drafts = Combo(store.Drafts()); body.Children.Add(drafts);
        body.Children.Add(Row(Button("继续", () => { draft = drafts.SelectedItem as LearningDraft ?? throw new InvalidOperationException("请选择草稿"); ShowDraft(); }),
            Button("删除草稿", () => { if (drafts.SelectedItem is LearningDraft chosen) store.DeleteDraft(chosen); ShowDraftPicker(); })));
    }
    private void ShowDraft()
    {
        if (draft is null) { ShowLearning(); return; }
        var body = TextPage($"{draft.Item.Name} · 参考 {draft.Samples.Count(s => !s.CheckOnly)} / 试识别 {draft.Samples.Count(s => s.CheckOnly)}"); page.Content = body;
        var samples = Combo(draft.Samples.Select((s, i) => new SampleChoice(s, $"{i + 1} · {(s.CheckOnly ? "试识别" : "参考")} · {s.EndSeconds - s.StartSeconds:0.00} 秒")));
        samples.SelectedIndex = samples.Items.Count > 0 ? 0 : -1; body.Children.Add(samples);
        PersonalSample Chosen() => (samples.SelectedItem as SampleChoice)?.Sample ?? throw new InvalidOperationException("请选择样本");
        body.Children.Add(Row(Button("试听", () => Play(store.ReadSample(Chosen()))), Button("裁剪", () => CropExisting(Chosen())),
            Button("删除", () => { store.RemoveSample(draft, Chosen().Id); ShowDraft(); }), Button("重新采样", () =>
            { draft = new() { Item = draft.Item, GroupId = draft.GroupId, IsNewItem = draft.IsNewItem, GroupConfirmed = draft.GroupConfirmed }; store.SaveDraft(draft); ShowDraft(); })));
        body.Children.Add(Row(Button("引导采样", () => Run(async () =>
        {
            player.Stop(); CaptureStarted?.Invoke(); var finished = await learning.Start(draft);
            if (controller.Mode == AssistantMode.Interaction)
            {
                CaptureFinished?.Invoke(); ShowDraft();
                if (finished && !operation!.IsCancellationRequested)
                    await BuildDraft();
            }
        })), Button("用当前片段", () => CropNew(selected?.Audio ?? throw new InvalidOperationException("当前片段已过期或尚无片段"), "识别片段")),
            Button("导入 WAV", ImportWave), Button("检查并启用", () => Run(BuildDraft))));
        Message(draft.Status);
    }
    private void ImportWave()
    {
        var dialog = new OpenFileDialog { Filter = "WAV 音频|*.wav" };
        if (dialog.ShowDialog(overlay) == true)
        {
            if (new FileInfo(dialog.FileName).Length > 64 * 1024 * 1024) throw new InvalidOperationException("请先将 WAV 裁剪到 64 MB 以内");
            CropNew(WaveAudio.Read(dialog.FileName), "WAV 导入 · " + Path.GetFileName(dialog.FileName));
        }
    }
    private void CropNew(AudioClip audio, string source) => CropEditor(audio, source, null);
    private void CropExisting(PersonalSample sample) => CropEditor(store.ReadSource(sample), sample.Source, sample);
    private void CropEditor(AudioClip audio, string source, PersonalSample? replace)
    {
        var body = TextPage($"裁剪片段 · 总长 {(double)audio.Samples.Length / audio.SampleRate:0.00} 秒，保留 0.19–5 秒"); page.Content = body;
        var start = new TextBox { Text = (replace?.StartSeconds ?? 0).ToString(CultureInfo.InvariantCulture), Width = 85, Padding = new Thickness(5) };
        var end = new TextBox { Text = (replace?.EndSeconds ?? Math.Min(5, (double)audio.Samples.Length / audio.SampleRate)).ToString(CultureInfo.InvariantCulture), Width = 85, Padding = new Thickness(5) };
        var check = new CheckBox { Content = "用作单独试识别", IsChecked = replace?.CheckOnly ?? false, Foreground = Theme.Text, Margin = new Thickness(5) };
        body.Children.Add(Row(Theme.Label("起始秒", 11), start, Theme.Label("结束秒", 11), end)); body.Children.Add(check);
        (double Start, double End) Range()
        {
            if (!double.TryParse(start.Text, CultureInfo.InvariantCulture, out var a) || !double.TryParse(end.Text, CultureInfo.InvariantCulture, out var b) || a < 0 || b <= a || b > (double)audio.Samples.Length / audio.SampleRate || b - a > 5)
                throw new InvalidOperationException("裁剪时间无效"); return (a, b);
        }
        body.Children.Add(Row(Button("试听选段", () => { var (a, b) = Range(); Play(new(audio.Samples.Skip((int)(a * audio.SampleRate)).Take((int)((b - a) * audio.SampleRate)).ToArray(), audio.SampleRate)); }),
            Button("保存样本", () =>
            {
                var (a, b) = Range();
                if (replace is null) store.AddSample(draft!, audio, source, a, b, check.IsChecked == true,
                    source == "识别片段" ? selected?.CaptureSession : null, source == "识别片段" ? selected?.CaptureStart : null, source == "识别片段" ? selected?.CaptureEnd : null);
                else store.TrimSample(draft!, replace, a, b, check.IsChecked == true);
                ShowDraft();
            }), Button("返回草稿", ShowDraft)));
    }
    private async Task BuildDraft()
    {
        if (draft is null) throw new InvalidOperationException("请先选择物品或草稿");
        player.Stop(); Message("正在建库和检查参考样本…");
        var token = operation!.Token; var built = await store.BuildDraft(draft, token); token.ThrowIfCancellationRequested();
        if (built.Conflicts.Length > 0) { ShowConflicts(built.Conflicts); return; }
        if (built.Version is null) { draft.Status = built.Message; store.SaveDraft(draft); ShowDraft(); Message(built.Message); return; }
        await activate(built.Version, false, token); draft.Status = "已启用 · " + built.Version.Library.Version; store.SaveDraft(draft); ShowDraft();
    }
    private void ShowConflicts(string[] ids)
    {
        var body = TextPage("与已有音效相近，请试听并确认同音关系"); page.Content = body;
        var groups = Combo(controller.Library.Groups.Where(g => ids.Contains(g.Id)), nameof(SoundGroup.Name)); body.Children.Add(groups);
        body.Children.Add(Row(Button("试听该组", () =>
        {
            var group = groups.SelectedItem as SoundGroup ?? throw new InvalidOperationException("请选择音效组");
            var sample = ReferenceAudio.Load(controller.LibraryRoot).Samples.First(s => s.GroupId == group.Id);
            Play(WaveAudio.Read(ReferenceAudio.VerifiedPath(controller.LibraryRoot, sample)));
        }), Button("确认同音并检查", () =>
        {
            draft!.GroupId = (groups.SelectedItem as SoundGroup)?.Id ?? throw new InvalidOperationException("请选择音效组");
            draft.GroupConfirmed = true; store.SaveDraft(draft); Run(BuildDraft);
        }), Button("暂不合并，返回补录", ShowDraft)));
    }
    private void ShowPersonal()
    {
        var profile = store.Profile(); var body = TextPage($"个人库 · {profile.Items.Count} 件新物品 / {profile.Samples.Count} 段录音"); page.Content = body;
        body.Children.Add(Row(Button("物品管理", () => ManageItems(profile)), Button("样本管理", () => ManageSamples(profile)),
            Button("继续草稿", ShowDraftPicker), Button("撤销更新", () => Run(async () =>
            { await activate(store.PreviousVersion(), true, operation!.Token); ShowPersonal(); Message("已恢复上一版本"); }))));
        body.Children.Add(Row(Button("导出个人库", () =>
        {
            var dialog = new SaveFileDialog { Filter = "个人音效库|*.aqtwlib", FileName = $"个人音效库-{DateTime.Now:yyyyMMdd}.aqtwlib" };
            if (dialog.ShowDialog(overlay) == true) { store.Export(dialog.FileName); Message("个人库已导出"); }
        }), Button("导入个人库", () =>
        {
            var dialog = new OpenFileDialog { Filter = "个人音效库|*.aqtwlib" };
            if (dialog.ShowDialog(overlay) == true) Run(async () => { var imported = await Task.Run(() => store.Import(dialog.FileName)); await BuildProfile(imported); });
        })));
        Message(store.RecoveryNotice.Length > 0 ? store.RecoveryNotice : "基础库独立保留，更新失败不会替换当前版本");
    }
    private void ManageItems(PersonalProfile profile)
    {
        var body = TextPage("个人新物品 · 禁用后不参与候选"); page.Content = body;
        var items = Combo(profile.Items.Select(i => new ItemChoice(i, $"{i.Item.Name} · {(i.Enabled ? "启用" : "禁用")}"))); body.Children.Add(items);
        PersonalItem Chosen() => (items.SelectedItem as ItemChoice)?.Item ?? throw new InvalidOperationException("请选择个人物品");
        body.Children.Add(Row(Button("启用 / 禁用", () => { var item = Chosen(); profile.Items[profile.Items.IndexOf(item)] = item with { Enabled = !item.Enabled }; Run(() => BuildProfile(profile)); }),
            Button("删除", () => { var item = Chosen(); profile.Items.Remove(item); profile.Samples.RemoveAll(s => s.ItemId == item.Item.Id); Run(() => BuildProfile(profile)); }),
            Button("补充样本", () => { var item = Chosen(); CreateDraft(item.Item, item.GroupId, true); })));
    }
    private void ManageSamples(PersonalProfile profile)
    {
        var body = TextPage("个人样本 · 参考参与识别，试识别片段不入索引"); page.Content = body;
        var samples = Combo(profile.Samples.Select(s => new SampleChoice(s, $"{controller.Library.Items.FirstOrDefault(i => i.Id == s.ItemId)?.Name ?? s.ItemId} · {(s.CheckOnly ? "试识别" : "参考")} · {(s.Enabled ? "启用" : "禁用")}")));
        body.Children.Add(samples);
        PersonalSample Chosen() => (samples.SelectedItem as SampleChoice)?.Sample ?? throw new InvalidOperationException("请选择样本");
        body.Children.Add(Row(Button("试听", () => Play(store.ReadSample(Chosen()))),
            Button("启用 / 禁用", () => { var sample = Chosen(); profile.Samples[profile.Samples.IndexOf(sample)] = sample with { Enabled = !sample.Enabled }; Run(() => BuildProfile(profile)); }),
            Button("删除", () => { profile.Samples.Remove(Chosen()); Run(() => BuildProfile(profile)); })));
    }
    private async Task BuildProfile(PersonalProfile profile)
    {
        Message("正在构建个人库版本…"); var token = operation!.Token;
        var built = await store.Build(profile, token); token.ThrowIfCancellationRequested();
        if (built.Version is not null) { await activate(built.Version, false, token); ShowPersonal(); } Message(built.Message);
    }
    public void Dispose() { disposed = true; Stop(); player.Dispose(); operation?.Dispose(); }
    private sealed record GroupChoice(string Id, string Label) { public override string ToString() => Label; }
    private sealed record ReferenceChoice(ReferenceSample Sample, string Label) { public override string ToString() => Label; }
    private sealed record SampleChoice(PersonalSample Sample, string Label) { public override string ToString() => Label; }
    private sealed record ItemChoice(PersonalItem Item, string Label) { public override string ToString() => Label; }
}

internal sealed class FitContent : Decorator
{
    private double scale = 1;
    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return new();
        var candidates = Child is ContentControl { Content: CandidatePanel };
        Child.Measure(new Size(constraint.Width, candidates ? constraint.Height : double.PositiveInfinity));
        scale = candidates || !double.IsFinite(constraint.Height) || Child.DesiredSize.Height == 0 ? 1 : Math.Min(1, constraint.Height / Child.DesiredSize.Height);
        Child.RenderTransform = new ScaleTransform(scale, scale);
        return new Size(Math.Min(constraint.Width, Child.DesiredSize.Width), Child.DesiredSize.Height * scale);
    }
    protected override Size ArrangeOverride(Size size)
    { Child?.Arrange(new Rect(0, 0, size.Width / Math.Max(.01, scale), size.Height / Math.Max(.01, scale))); return size; }
}
