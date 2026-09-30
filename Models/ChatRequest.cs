namespace Onudhabon.Models;

public sealed class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public List<ChatMessageItem>? History { get; set; }
    public Guid? DocumentId { get; set; }
    public string StudyMode { get; set; } = "ask";
    public string Difficulty { get; set; } = "intermediate";
    public string ClassLevel { get; set; } = string.Empty;
}

public sealed class ChatMessageItem
{
    public string Role { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
