using Listener.Core;

try
{
    if (args.Length < 2) throw new ArgumentException("用法：build 清单.json 输出库.json | match 库.json 音频.wav | scan 库.json 音频.wav 报告.json | evaluate 库.json 测试清单.json 报告.json | calibrate 库.json 校准清单.json 输出库.json | coverage 库.json");
    switch (args[0])
    {
        case "build" when args.Length == 3:
            var built = LibraryBuilder.Build(args[1]); JsonFile.Write(args[2], built);
            Console.WriteLine($"已构建 {built.Groups.Count} 组 / {built.Items.Count} 件物品 / {built.Groups.Sum(g => g.Templates.Count)} 个样本"); break;
        case "match" when args.Length == 3:
            var clip = WaveAudio.Read(args[2]);
            using (var recognizer = RecognizerFactory.Create(JsonFile.Read<SoundLibrary>(args[1]), Path.GetDirectoryName(Path.GetFullPath(args[1]))))
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(recognizer.Recognize(clip.Samples, clip.SampleRate), JsonFile.Options)); break;
        case "evaluate" when args.Length == 4:
            var report = Evaluation.Run(JsonFile.Read<SoundLibrary>(args[1]), args[2], Path.GetDirectoryName(Path.GetFullPath(args[1]))); JsonFile.Write(args[3], report);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, JsonFile.Options)); break;
        case "scan" when args.Length == 4:
            using (var scannerRecognizer = RecognizerFactory.Create(JsonFile.Read<SoundLibrary>(args[1]), Path.GetDirectoryName(Path.GetFullPath(args[1]))))
            {
                var events = StreamEvaluation.Run(scannerRecognizer, WaveAudio.Read(args[2]));
                JsonFile.Write(args[3], events);
                Console.WriteLine($"已分析 {events.Count} 个声音事件，{events.Count(entry => entry.Status == RecognitionStatus.Matched)} 个匹配；报告：{args[3]}");
            }
            break;
        case "calibrate" when args.Length == 4:
            JsonFile.Write(args[3], Evaluation.Calibrate(JsonFile.Read<SoundLibrary>(args[1]), args[2], Path.GetDirectoryName(Path.GetFullPath(args[1])))); break;
        case "coverage" when args.Length == 2:
            var lib = JsonFile.Read<SoundLibrary>(args[1]); lib.Validate();
            Console.WriteLine($"版本 {lib.Version}，状态 {lib.ValidationStatus}");
            foreach (var group in lib.Groups) Console.WriteLine($"{group.Name} | {group.Action} | 样本 {group.Templates.Count} | " + string.Join("、", group.ItemIds.Select(id => lib.Items.Single(i => i.Id == id).Name)));
            foreach (var item in lib.Items.Where(i => !lib.Groups.Any(g => g.ItemIds.Contains(i.Id) && g.Templates.Count > 0))) Console.WriteLine("缺失样本：" + item.Name);
            break;
        default: throw new ArgumentException("命令或参数数量不正确。");
    }
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
