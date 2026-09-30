namespace Onudhabon.Models;

public sealed class AiProgressAnalyzeInput
{
    public int StudentId { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class AiProgressDraft
{
    public List<AiProgressDraftItem> Items { get; set; } = new();
    public List<string> NeedsClarification { get; set; } = new();
}

public sealed class AiProgressDraftItem
{
    public string SubjectName { get; set; } = string.Empty;
    public int LectureNumber { get; set; }
    public string Topic { get; set; } = string.Empty;
    public double? Marks { get; set; }
    public string? Grade { get; set; }
    public string? Remarks { get; set; }
}

public sealed class AiProgressApplyInput
{
    public int StudentId { get; set; }
    public List<AiProgressDraftItem> Items { get; set; } = new();
}
