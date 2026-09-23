namespace Listener.Core;

public static class AutomaticRecognitionConsensus
{
    public static RecognitionAnalysis Resolve(RecognitionAnalysis whole, RecognitionAnalysis focused)
    {
        var first = whole.Result;
        var second = focused.Result;
        if (first.Status == RecognitionStatus.Unknown && second.Status == RecognitionStatus.Matched &&
            second.BestMatch is { Score: >= .86 } focusedBest &&
            whole.Scores.FirstOrDefault() is { Score: >= .68 } wholeBest &&
            wholeBest.GroupId == focusedBest.GroupId)
            return focused with { Result = second with
            {
                Candidates = second.Candidates.Select(candidate => candidate with { Tag = RecognitionTag.Suspected }).ToArray(),
                Message = "音效匹配 · 已聚焦声音事件，请试听核对"
            } };
        if (first.Status != RecognitionStatus.Matched) return whole;
        var stable = second.Status == RecognitionStatus.Matched &&
            first.BestMatch?.GroupId == second.BestMatch?.GroupId;
        if (!stable)
            return whole with { Result = first with
            {
                Status = RecognitionStatus.Unknown, Candidates = [], Message = "识别失败 · 两次分析候选不一致"
            } };
        var focusedExact = second.Candidates.Where(candidate => candidate.Tag == RecognitionTag.Exact)
            .Select(candidate => candidate.Item.Id).ToHashSet();
        return whole with { Result = first with
        {
            Candidates = first.Candidates.Select(candidate => candidate.Tag == RecognitionTag.Exact && !focusedExact.Contains(candidate.Item.Id)
                ? candidate with { Tag = RecognitionTag.Suspected } : candidate).ToArray()
        } };
    }
}
