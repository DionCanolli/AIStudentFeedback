namespace AIFeedbackSystem.Models;

public class StudentFeedback
{
    public int Id { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string CommentText { get; set; } = string.Empty;
    public string? SentimentScore { get; set; }
    public string? MainTopic { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}