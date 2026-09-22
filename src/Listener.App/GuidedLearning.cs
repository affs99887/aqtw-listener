namespace Listener.App;

internal sealed class GuidedLearning(ListeningController controller, PersonalLibraryStore store, Func<int, CancellationToken, Task>? delay = null)
{
    private CancellationTokenSource? operation;
    public bool Running => operation is not null;
    public event Action<string>? Progress;
    public event Action<LearningDraft>? Completed;
    private Task Delay(int milliseconds, CancellationToken token) => delay?.Invoke(milliseconds, token) ?? Task.Delay(milliseconds, token);
    public void Pause() => operation?.Cancel();
    public async Task<bool> Start(LearningDraft draft)
    {
        if (Running) return false;
        using var run = new CancellationTokenSource(); operation = run;
        draft.Guided = true; store.SaveDraft(draft);
        try
        {
            await controller.SetMode(AssistantMode.Learning);
            while (draft.Samples.Count(s => s.Enabled && !s.CheckOnly) < 3 || !draft.Samples.Any(s => s.Enabled && s.CheckOnly))
            {
                run.Token.ThrowIfCancellationRequested();
                while (!controller.ReadyForLearning)
                {
                    Progress?.Invoke(controller.LearningUnavailableReason);
                    await Delay(100, run.Token);
                }
                var check = draft.Samples.Count(s => s.Enabled && !s.CheckOnly) >= 3;
                var round = check ? "第 4 轮 · 单独试识别" : $"第 {draft.Samples.Count(s => s.Enabled && !s.CheckOnly) + 1} / 3 轮参考采样";
                var interrupted = false;
                for (var tick = 30; tick > 0; tick--)
                {
                    if (!controller.ReadyForLearning) { interrupted = true; break; }
                    Progress?.Invoke($"{round} · {(tick + 9) / 10} 秒后拿起并保持"); await Delay(100, run.Token);
                }
                if (interrupted) continue;
                var start = controller.Now;
                Progress?.Invoke($"{round} · 现在拿起并保持，不要放下");
                for (var tick = 0; tick < 13; tick++)
                {
                    await Delay(100, run.Token);
                    if (!controller.ReadyForLearning) { interrupted = true; break; }
                }
                if (interrupted) continue;
                var clip = controller.LearningSlice(start, start + 1.2);
                try
                {
                    store.AddSample(draft, clip, "引导录制 · 拾起并保持", checkOnly: check,
                        captureSession: controller.CaptureSession, captureStart: start, captureEnd: start + 1.2);
                    Progress?.Invoke("本轮已保存，现在可以放下物品");
                }
                catch (InvalidDataException ex) { Progress?.Invoke("本轮补录 · " + ex.Message); }
                await Delay(1800, run.Token);
            }
            await controller.SetMode(AssistantMode.Interaction); Completed?.Invoke(draft); return true;
        }
        catch (OperationCanceledException)
        { draft.Status = "采样已暂停，可继续"; store.SaveDraft(draft); Progress?.Invoke(draft.Status); return false; }
        catch (Exception ex)
        { draft.Status = "采样失败 · " + ex.Message; store.SaveDraft(draft); Progress?.Invoke(draft.Status); return false; }
        finally
        {
            operation = null;
            if (controller.Mode == AssistantMode.Learning) await controller.SetMode(AssistantMode.Interaction);
        }
    }
}
