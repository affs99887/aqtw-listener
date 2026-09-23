using System.Globalization;
using Microsoft.Win32;

namespace Listener.App;

// Sound and every matching item share one adaptive surface; library editors remain available here.
internal sealed class OverlayWorkspace : Border, IDisposable
{
    private static readonly DependencyProperty BaseFontSizeProperty = DependencyProperty.RegisterAttached(
        "BaseFontSize", typeof(double), typeof(OverlayWorkspace), new PropertyMetadata(double.NaN));
    private readonly ListeningController controller;
    private readonly PersonalLibraryStore store;
    private readonly OverlayWindow overlay;
    private readonly Settings settings;
    private readonly Func<LibraryVersion, bool, CancellationToken, Task> activate;
    private readonly Func<Task> enterInteraction;
    private readonly AudioPreviewPlayer player = new();
    private readonly GuidedLearning learning;
    private readonly ContentControl page = new();
    private readonly DockPanel nav = new();
    private readonly TextBlock notice = Theme.Label("", 11, Theme.Accent);
    private readonly List<Button> navigation = [];
    private readonly FitContent fittedPage;
    private CandidatePanel? soundPanel;
    private AnalysisSnapshot? selected;
    private RecognitionResult? liveResult;
    private LearningDraft? draft;
    private CancellationTokenSource? operation;
    private bool busy, disposed;
    public event Action? CaptureStarted;
    public event Action? CaptureFinished;
    public event Action? LayoutChanged;
    public bool Learning => learning.Running;
    internal string StatusText => notice.Text;
    internal int CandidateCount => soundPanel?.CandidateCount ?? 0;
    internal bool IsPlaying => player.IsPlaying;
    public OverlayWorkspace(ListeningController controller, PersonalLibraryStore store, OverlayWindow overlay, Settings settings,
        Func<LibraryVersion, bool, CancellationToken, Task> activate, Func<Task> enterInteraction)
    {
        this.controller = controller; this.store = store; this.overlay = overlay; this.settings = settings;
        this.activate = activate; this.enterInteraction = enterInteraction;
        learning = new(controller, store); Background = Theme.Background; Padding = new Thickness(8);
        var body = new DockPanel(); Child = body;
        navigation.AddRange([Button("监听浮窗", ShowSound), Button("候选详情", ShowCandidates), Button("补库 / 学习", ShowLearning), Button("个人库", ShowPersonal)]);
        var sections = Row(navigation.ToArray());
        DockPanel.SetDock(sections, Dock.Left); nav.Children.Add(sections);
        var detail = Button("详情", () => MessageBox.Show(overlay, notice.Text, "当前状态"));
        navigation.Add(detail);
        DockPanel.SetDock(detail, Dock.Right); nav.Children.Add(detail);
        notice.FontSize = 10 * settings.FontScale; notice.TextWrapping = TextWrapping.NoWrap;
        notice.TextTrimming = TextTrimming.CharacterEllipsis; notice.VerticalAlignment = VerticalAlignment.Center;
        notice.Margin = new Thickness(7, 0, 2, 0); nav.Children.Add(notice);
        DockPanel.SetDock(nav, Dock.Top); body.Children.Add(nav);
        fittedPage = new FitContent { ContentView = page }; body.Children.Add(fittedPage);
        player.Status += Message;
        learning.Progress += text => { Message(text); SetPage(TextPage(draft?.Item.Name ?? "学习采样", text, "Ctrl+Alt+C 暂停并操作浮窗")); };
        ShowSound();
    }
    private Button Button(string text, Action action)
    {
        var button = OverlayWindow.SmallButton(text, () => { if (!busy) { try { action(); } catch (Exception ex) { Message(ex.Message); } } });
        button.FontSize = 11 * FontScale; button.SetValue(BaseFontSizeProperty, 11d); return button;
    }
    private static WrapPanel Row(params UIElement[] elements)
    { var row = new WrapPanel(); foreach (var element in elements) row.Children.Add(element); return row; }
    private StackPanel TextPage(params string[] lines)
    {
        var stack = new StackPanel();
        foreach (var line in lines)
        {
            var label = Theme.Label(line, 12 * FontScale); label.SetValue(BaseFontSizeProperty, 12d); stack.Children.Add(label);
        }
        return stack;
    }
    private ComboBox Combo<T>(IEnumerable<T> values, string? display = null)
    {
        var combo = new ComboBox { FontSize = 12 * FontScale, ItemsSource = values.ToArray(), DisplayMemberPath = display ?? "",
            Margin = new Thickness(0, 2, 0, 3), MinHeight = 28 };
        combo.SetValue(BaseFontSizeProperty, 12d); return combo;
    }
    private void Message(string text)
    {
        notice.Text = text; notice.ToolTip = text;
        overlay.SetHeaderStatus(player.IsPlaying ? "试听中 · 采音已暂停" :
            controller.Mode == AssistantMode.Listening ? "监听中 · 等待声音" : "采音已暂停 · 可试听");
    }
    private double FontScale => double.IsFinite(settings.FontScale) ? Math.Clamp(settings.FontScale, .9, 1.15) : 1;
    private void SetPage(UIElement view, bool sound = false)
    {
        if (!sound) soundPanel = null;
        nav.Visibility = sound ? Visibility.Collapsed : Visibility.Visible;
        Padding = sound ? new Thickness(0) : new Thickness(8);
        fittedPage.AdaptiveContent = sound; page.Content = view;
        if (!sound) Dispatcher.BeginInvoke(() =>
        {
            if (!disposed && ReferenceEquals(page.Content, view)) RefreshEditorFonts();
        });
        LayoutChanged?.Invoke();
    }
    public void RefreshAppearance()
    {
        notice.FontSize = 10 * FontScale;
        foreach (var button in navigation) button.FontSize = 11 * FontScale;
        if (soundPanel is not null)
        {
            var playbackMessage = player.IsPlaying ? notice.Text : null;
            ShowSound(); if (playbackMessage is not null) Message(playbackMessage);
        }
        else RefreshEditorFonts();
        InvalidateMeasure(); LayoutChanged?.Invoke();
    }
    private void RefreshEditorFonts()
    {
        if (page.Content is not DependencyObject root) return;
        // Snapshot logical font owners before changing inherited values. Template
        // children inherit their control's font and must not be scaled a second time.
        var owners = FontOwners(root).ToArray();
        foreach (var (element, property, currentSize) in owners)
            if (double.IsNaN((double)element.GetValue(BaseFontSizeProperty))) element.SetValue(BaseFontSizeProperty, currentSize);
        foreach (var (element, property, _) in owners)
            element.SetValue(property, (double)element.GetValue(BaseFontSizeProperty) * FontScale);
        fittedPage.InvalidateMeasure(); LayoutChanged?.Invoke();
    }
    private static IEnumerable<(DependencyObject Element, DependencyProperty Property, double Size)> FontOwners(DependencyObject root)
    {
        if (root is TextBlock text) yield return (text, TextBlock.FontSizeProperty, text.FontSize);
        else if (root is Control control) yield return (control, Control.FontSizeProperty, control.FontSize);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var owner in FontOwners(child)) yield return owner;
    }
    public double GetPreferredWidth(double maximumWidth, double maximumHeight)
    {
        var width = soundPanel?.GetPreferredWidth(Math.Max(1, maximumWidth - 10), Math.Max(1, maximumHeight - 44 * FontScale)) + 10
            ?? Math.Min(maximumWidth, 420 * FontScale);
        return Math.Min(maximumWidth, Math.Max(Math.Min(maximumWidth, 360 * FontScale), width));
    }
    private async void Run(Func<Task> work)
    {
        if (busy || disposed) return;
        busy = true; operation?.Dispose(); operation = new();
        try { await work(); }
        catch (OperationCanceledException) { Message("操作已取消，原音效库保持可用"); }
        catch (Exception ex) { Message(ex.Message); }
        finally { busy = false; }
    }
    public async void Select(AnalysisSnapshot? snapshot)
    {
        try
        {
            if (snapshot is not null && controller.Mode == AssistantMode.Listening) await enterInteraction();
            player.Stop(); selected = snapshot ?? controller.Audio.Recent.FirstOrDefault();
            controller.Audio.Pin(selected?.Id); ShowSound();
        }
        catch (Exception ex) { Message(ex.Message); }
    }
    public void UpdateListeningResult(RecognitionResult? result)
    {
        liveResult = result;
        if (controller.Mode != AssistantMode.Listening) return;
        var id = controller.History.Latest?.SnapshotId;
        selected = id.HasValue ? controller.Audio.Get(id.Value) : null;
        ShowSound();
    }
    public void UpdateListeningActivity(RecognitionActivity activity)
    {
        soundPanel?.SetActivity(activity);
        if (controller.Mode == AssistantMode.Listening && !player.IsPlaying)
            overlay.SetHeaderStatus(activity.Message);
    }
    public void OpenLearning() => ShowLearning();
    public void PauseLearning() { learning.Pause(); player.Stop(); }
    public void Stop()
    { operation?.Cancel(); learning.Pause(); player.Stop(); controller.Audio.Pin(null); }
    private async Task Play(AudioClip clip)
    {
        if (controller.Mode != AssistantMode.Interaction) await enterInteraction();
        if (controller.Mode != AssistantMode.Interaction) throw new InvalidOperationException("无法暂停采音，请稍后再试");
        player.Play(clip, settings.DeviceId);
    }
    private async void PlayFromButton(AudioClip clip)
    {
        try { await Play(clip); }
        catch (Exception ex) { Message(ex.Message); }
    }
    private void ShowSound()
    {
        var snapshots = controller.Audio.Recent.Concat(controller.History.Entries.Select(e => controller.Audio.Get(e.SnapshotId)).OfType<AnalysisSnapshot>())
            .Concat(selected is null ? [] : new[] { selected }).DistinctBy(s => s.Id).OrderByDescending(s => s.At).ToArray();
        if (controller.Mode == AssistantMode.Listening)
        {
            var liveId = controller.History.Latest?.SnapshotId;
            selected = liveId.HasValue ? controller.Audio.Get(liveId.Value) : null;
        }
        else selected ??= snapshots.FirstOrDefault();
        controller.Audio.Pin(selected?.Id);
        var library = selected?.Library ?? controller.Library; var root = selected?.LibraryRoot ?? controller.LibraryRoot;
        var references = LoadPlayableReferences(root);
        var panel = new CandidatePanel(root, settings, interactive: true, compact: true);
        var chosenVersions = new Dictionary<string, int>();
        async void PreviewReference(Candidate candidate, int index)
        {
            try
            {
                var samples = references.GetValueOrDefault(candidate.GroupId) ?? [];
                if (samples.Length == 0) throw new InvalidOperationException("该物品尚无可试听的参考音效");
                index = Math.Clamp(index, 0, samples.Length - 1); chosenVersions[candidate.GroupId] = index;
                await Play(WaveAudio.Read(ReferenceAudio.VerifiedPath(root, samples[index])));
                Message($"试听参考 · {candidate.Item.Name} · {index + 1}/{samples.Length} · 同音组共享，采音已暂停");
            }
            catch (Exception ex) { Message(ex.Message); }
        }
        panel.SetAudioPreview(candidate => PreviewReference(candidate, chosenVersions.GetValueOrDefault(candidate.GroupId)),
            candidate => references.ContainsKey(candidate.GroupId));
        panel.SetReferenceChoices(candidate =>
        {
            var samples = references.GetValueOrDefault(candidate.GroupId) ?? [];
            if (samples.Length <= 1) return null;
            var menu = new ContextMenu { Background = Theme.Panel, Foreground = Theme.Text, BorderBrush = Theme.Line };
            foreach (var number in Enumerable.Range(0, samples.Length))
            {
                var option = new MenuItem { Header = $"参考 {number + 1} · {candidate.Item.Name}", IsCheckable = true,
                    IsChecked = number == chosenVersions.GetValueOrDefault(candidate.GroupId), Background = Theme.Panel, Foreground = Theme.Text };
                option.Click += (_, _) => PreviewReference(candidate, number); menu.Items.Add(option);
            }
            menu.Opened += (_, _) =>
            {
                var current = chosenVersions.GetValueOrDefault(candidate.GroupId);
                for (var index = 0; index < menu.Items.Count; index++)
                    ((MenuItem)menu.Items[index]).IsChecked = index == current;
            };
            return menu;
        });
        var replay = Button("▶ 回放当前声音", () => PlayFromButton(selected!.Audio!));
        var save = Button("保存 WAV", SaveClip);
        replay.IsEnabled = save.IsEnabled = selected?.Audio is { Samples.Length: > 0 };
        replay.ToolTip = replay.IsEnabled ? "回放本次采集的声音；试听期间暂停采音" : "尚无可回放的录音，或该片段已过期";
        save.ToolTip = save.IsEnabled ? "将当前录音保存为 WAV 文件" : "没有可保存的录音";
        ToolTipService.SetShowOnDisabled(replay, true); ToolTipService.SetShowOnDisabled(save, true);
        replay.BorderBrush = Theme.Accent;
        var stop = Button("■ 停止", () => { player.Stop(); Message(controller.Mode == AssistantMode.Listening
            ? "播放已停止 · 继续听音" : "播放已停止 · 采音仍暂停"); });
        var back = Button("返回监听", () => { player.Stop(); overlay.ReturnToListening(); });
        back.IsEnabled = controller.Mode != AssistantMode.Listening;
        if (!back.IsEnabled) back.Content = "监听中";
        back.ToolTip = back.IsEnabled ? "停止试听并继续采音" : "当前正在持续监听";
        var actions = new[] { (Button: replay, Weight: 290d), (Button: stop, Weight: 235d),
            (Button: save, Weight: 235d), (Button: back, Weight: 270d) };
        var toolbar = new Grid { Margin = new Thickness(10, 3, 4, 3), HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.SizeChanged += (_, _) => toolbar.Width = Math.Max(1, panel.ActualWidth - 14);
        for (var index = 0; index < actions.Length; index++)
        {
            var (button, weight) = actions[index];
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star) });
            button.Height = 34; button.Padding = new Thickness(0);
            button.Margin = new Thickness(0, 0, 6, 0); button.FontSize = 11 * FontScale;
            Grid.SetColumn(button, index); toolbar.Children.Add(button);
        }
        panel.SetToolbar(toolbar);
        panel.ShowResult(controller.Mode == AssistantMode.Listening ? liveResult : selected?.Analysis.Result,
            listening: controller.Mode == AssistantMode.Listening && controller.Enabled);
        panel.SetActivity(controller.CurrentActivity);

        var body = new DockPanel();
        var options = new DockPanel { Height = panel.CandidateCount > 20 ? 26 : 40,
            Background = Theme.Panel, LastChildFill = true };
        Button? clips = null;
        clips = Button(selected is null ? "暂无录音" : $"{selected.At:HH:mm:ss} 的录音 ▾", () =>
        {
            var menu = new ContextMenu { Background = Theme.Panel, Foreground = Theme.Text, BorderBrush = Theme.Line };
            foreach (var snapshot in snapshots)
            {
                var option = new MenuItem { Header = snapshot.ToString(), IsCheckable = true, IsChecked = snapshot.Id == selected?.Id,
                    Background = Theme.Panel, Foreground = Theme.Text };
                option.Click += (_, _) => Select(snapshot); menu.Items.Add(option);
            }
            clips!.ContextMenu = menu; menu.PlacementTarget = clips; menu.IsOpen = true;
        });
        clips.FontSize = 10 * FontScale; clips.Margin = new Thickness(4, 0, 8, 0);
        clips.Background = Brushes.Transparent; clips.BorderThickness = new Thickness(0);
        clips.ToolTip = "选择本次会话中的声音片段"; clips.IsEnabled = snapshots.Length > 0;
        DockPanel.SetDock(clips, Dock.Right); options.Children.Add(clips);
        var nearNote = Theme.Label("大红概率按同格候选计算，非真实出货概率；匹配度仅针对音效。", 10 * FontScale, Theme.Muted);
        nearNote.Margin = new Thickness(12, 0, 0, 0); nearNote.VerticalAlignment = VerticalAlignment.Center;
        nearNote.TextWrapping = TextWrapping.NoWrap; nearNote.TextTrimming = TextTrimming.CharacterEllipsis; options.Children.Add(nearNote);
        DockPanel.SetDock(options, Dock.Bottom); body.Children.Add(options); body.Children.Add(panel);
        soundPanel = panel; SetPage(body, sound: true); panel.LayoutChanged += () => LayoutChanged?.Invoke();
        Message(controller.Mode == AssistantMode.Listening
            ? selected is null ? controller.Enabled ? "监听中 · 等待声音" : "监听已关闭"
                : $"监听中 · {selected.At:HH:mm:ss} · 结果已保留，继续听音"
            : selected is null ? "采音已暂停 · 尚无声音片段；可进入补库选择物品"
                : $"采音已暂停 · {selected.At:HH:mm:ss} · {selected.Analysis.Result.Message}");
    }
    private static Dictionary<string, ReferenceSample[]> LoadPlayableReferences(string root)
    {
        try
        {
            return ReferenceAudio.Load(root).Samples.Where(sample =>
            {
                try { _ = ReferenceAudio.VerifiedPath(root, sample); return true; }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { return false; }
            }).GroupBy(sample => sample.GroupId).ToDictionary(group => group.Key, group => group.ToArray());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) { return []; }
    }
    private void SaveClip()
    {
        var audio = selected?.Audio ?? throw new InvalidOperationException("片段已过期或尚未收到声音");
        var dialog = new SaveFileDialog { Filter = "WAV 音频|*.wav", FileName = $"声音-{selected!.At:yyyyMMdd-HHmmss}.wav" };
        if (dialog.ShowDialog(overlay) == true) { WaveAudio.Write(dialog.FileName, audio.Samples, audio.SampleRate); Message("声音已保存"); }
    }
    private void ShowCandidates()
    {
        ShowSound();
    }
    private void ShowLearning()
    {
        var body = TextPage("先确认实际拖动的物品，再录制或添加样本"); SetPage(body);
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
        var body = TextPage("请选择要补充的音效组"); SetPage(body);
        var choice = Combo(groups, nameof(SoundGroup.Name)); body.Children.Add(choice);
        body.Children.Add(Button("确认", () => CreateDraft(item, (choice.SelectedItem as SoundGroup)?.Id ?? throw new InvalidOperationException("请选择音效组"), false)));
    }
    private void CreateDraft(ItemDefinition item, string groupId, bool newItem)
    { draft = new() { Item = item, GroupId = groupId, IsNewItem = newItem }; store.SaveDraft(draft); ShowDraft(); }
    private void NewItem()
    {
        var body = TextPage("新物品 · 第 1 / 2 步"); SetPage(body);
        var name = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 2, 0, 4) };
        var rarity = Combo(new[] { "大红", "非大红" }); body.Children.Add(name); body.Children.Add(rarity);
        name.ToolTip = "物品名称";
        body.Children.Add(Button("下一步：格数和图片", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || rarity.SelectedIndex < 0) throw new InvalidOperationException("请填写名称并选择分类");
            NewItemDetails(name.Text.Trim(), rarity.SelectedIndex == 0);
        }));
    }
    private void NewItemDetails(string name, bool gold)
    {
        var body = TextPage("新物品 · 第 2 / 2 步：宽 × 高（格）"); SetPage(body);
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
        var body = TextPage("本地学习草稿"); SetPage(body);
        var drafts = Combo(store.Drafts()); body.Children.Add(drafts);
        body.Children.Add(Row(Button("继续", () => { draft = drafts.SelectedItem as LearningDraft ?? throw new InvalidOperationException("请选择草稿"); ShowDraft(); }),
            Button("删除草稿", () => { if (drafts.SelectedItem is LearningDraft chosen) store.DeleteDraft(chosen); ShowDraftPicker(); })));
    }
    private void ShowDraft()
    {
        if (draft is null) { ShowLearning(); return; }
        var body = TextPage($"{draft.Item.Name} · 参考 {draft.Samples.Count(s => !s.CheckOnly)} / 试识别 {draft.Samples.Count(s => s.CheckOnly)}"); SetPage(body);
        var samples = Combo(draft.Samples.Select((s, i) => new SampleChoice(s, $"{i + 1} · {(s.CheckOnly ? "试识别" : "参考")} · {s.EndSeconds - s.StartSeconds:0.00} 秒")));
        samples.SelectedIndex = samples.Items.Count > 0 ? 0 : -1; body.Children.Add(samples);
        PersonalSample Chosen() => (samples.SelectedItem as SampleChoice)?.Sample ?? throw new InvalidOperationException("请选择样本");
        body.Children.Add(Row(Button("试听", () => PlayFromButton(store.ReadSample(Chosen()))), Button("裁剪", () => CropExisting(Chosen())),
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
        var body = TextPage($"裁剪片段 · 总长 {(double)audio.Samples.Length / audio.SampleRate:0.00} 秒，保留 0.19–5 秒"); SetPage(body);
        var start = new TextBox { Text = (replace?.StartSeconds ?? 0).ToString(CultureInfo.InvariantCulture), Width = 85, Padding = new Thickness(5) };
        var end = new TextBox { Text = (replace?.EndSeconds ?? Math.Min(5, (double)audio.Samples.Length / audio.SampleRate)).ToString(CultureInfo.InvariantCulture), Width = 85, Padding = new Thickness(5) };
        var check = new CheckBox { Content = "用作单独试识别", IsChecked = replace?.CheckOnly ?? false, Foreground = Theme.Text, Margin = new Thickness(5) };
        body.Children.Add(Row(Theme.Label("起始秒", 11), start, Theme.Label("结束秒", 11), end)); body.Children.Add(check);
        (double Start, double End) Range()
        {
            if (!double.TryParse(start.Text, CultureInfo.InvariantCulture, out var a) || !double.TryParse(end.Text, CultureInfo.InvariantCulture, out var b) || a < 0 || b <= a || b > (double)audio.Samples.Length / audio.SampleRate || b - a > 5)
                throw new InvalidOperationException("裁剪时间无效"); return (a, b);
        }
        body.Children.Add(Row(Button("试听选段", () => { var (a, b) = Range(); PlayFromButton(new(audio.Samples.Skip((int)(a * audio.SampleRate)).Take((int)((b - a) * audio.SampleRate)).ToArray(), audio.SampleRate)); }),
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
        var body = TextPage("与已有音效相近，请试听并确认同音关系"); SetPage(body);
        var groups = Combo(controller.Library.Groups.Where(g => ids.Contains(g.Id)), nameof(SoundGroup.Name)); body.Children.Add(groups);
        body.Children.Add(Row(Button("试听该组", () =>
        {
            var group = groups.SelectedItem as SoundGroup ?? throw new InvalidOperationException("请选择音效组");
            var sample = ReferenceAudio.Load(controller.LibraryRoot).Samples.First(s => s.GroupId == group.Id);
            PlayFromButton(WaveAudio.Read(ReferenceAudio.VerifiedPath(controller.LibraryRoot, sample)));
        }), Button("确认同音并检查", () =>
        {
            draft!.GroupId = (groups.SelectedItem as SoundGroup)?.Id ?? throw new InvalidOperationException("请选择音效组");
            draft.GroupConfirmed = true; store.SaveDraft(draft); Run(BuildDraft);
        }), Button("暂不合并，返回补录", ShowDraft)));
    }
    private void ShowPersonal()
    {
        var profile = store.Profile(); var body = TextPage($"个人库 · {profile.Items.Count} 件新物品 / {profile.Samples.Count} 段录音"); SetPage(body);
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
        var body = TextPage("个人新物品 · 禁用后不参与候选"); SetPage(body);
        var items = Combo(profile.Items.Select(i => new ItemChoice(i, $"{i.Item.Name} · {(i.Enabled ? "启用" : "禁用")}"))); body.Children.Add(items);
        PersonalItem Chosen() => (items.SelectedItem as ItemChoice)?.Item ?? throw new InvalidOperationException("请选择个人物品");
        body.Children.Add(Row(Button("启用 / 禁用", () => { var item = Chosen(); profile.Items[profile.Items.IndexOf(item)] = item with { Enabled = !item.Enabled }; Run(() => BuildProfile(profile)); }),
            Button("删除", () => { var item = Chosen(); profile.Items.Remove(item); profile.Samples.RemoveAll(s => s.ItemId == item.Item.Id); Run(() => BuildProfile(profile)); }),
            Button("补充样本", () => { var item = Chosen(); CreateDraft(item.Item, item.GroupId, true); })));
    }
    private void ManageSamples(PersonalProfile profile)
    {
        var body = TextPage("个人样本 · 参考参与识别，试识别片段不入索引"); SetPage(body);
        var samples = Combo(profile.Samples.Select(s => new SampleChoice(s, $"{controller.Library.Items.FirstOrDefault(i => i.Id == s.ItemId)?.Name ?? s.ItemId} · {(s.CheckOnly ? "试识别" : "参考")} · {(s.Enabled ? "启用" : "禁用")}")));
        body.Children.Add(samples);
        PersonalSample Chosen() => (samples.SelectedItem as SampleChoice)?.Sample ?? throw new InvalidOperationException("请选择样本");
        body.Children.Add(Row(Button("试听", () => PlayFromButton(store.ReadSample(Chosen()))),
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
    private sealed record SampleChoice(PersonalSample Sample, string Label) { public override string ToString() => Label; }
    private sealed record ItemChoice(PersonalItem Item, string Label) { public override string ToString() => Label; }
}

internal sealed class FitContent : Border
{
    private readonly ScrollViewer editor = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        CanContentScroll = false, Padding = new Thickness(0), BorderThickness = new Thickness(0)
    };
    private UIElement? content;
    private bool adaptive;
    public UIElement? ContentView
    {
        get => content;
        set { content = value; UpdatePresenter(); }
    }
    public bool AdaptiveContent
    {
        get => adaptive;
        set { if (adaptive == value) return; adaptive = value; UpdatePresenter(); }
    }
    private void UpdatePresenter()
    {
        Child = null; editor.Content = null;
        if (adaptive) Child = content;
        else { editor.Content = content; Child = editor; }
    }
}
