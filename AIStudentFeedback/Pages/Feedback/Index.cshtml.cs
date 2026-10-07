using AIFeedbackSystem.Data;
using AIFeedbackSystem.Models;
using AIFeedbackSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIFeedbackSystem.Pages.Feedback;

public class IndexModel : PageModel
{
    private readonly FeedbackContext _db;
    private readonly NlpService _nlp;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(FeedbackContext db, NlpService nlp, ILogger<IndexModel> logger)
    {
        _db = db;
        _nlp = nlp;
        _logger = logger;
    }

    [BindProperty] public string StudentName { get; set; } = string.Empty;
    [BindProperty] public string CommentText { get; set; } = string.Empty;
    public string? Message { get; set; }
    public bool MessageIsWarning { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(CommentText))
        {
            Message = "Please enter a comment.";
            return Page();
        }

        var (sentiment, topic, analyzed) = await _nlp.AnalyzeAsync(CommentText);

        _db.Feedbacks.Add(new StudentFeedback
        {
            StudentName = string.IsNullOrWhiteSpace(StudentName) ? "Anonymous" : StudentName,
            CommentText = CommentText,
            SentimentScore = sentiment,
            MainTopic = topic,
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save feedback.");
            Message = DatabaseErrorMessage(ex);
            MessageIsWarning = true;
            return Page();
        }

        Message = analyzed
            ? "Thanks! Your feedback was saved."
            : "Thanks! Your feedback was saved. Sentiment and topic were estimated locally because Hugging Face did not return an analysis.";
        MessageIsWarning = false;
        StudentName = string.Empty;
        CommentText = string.Empty;
        return Page();
    }

    private static string DatabaseErrorMessage(Exception ex)
    {
        var message = ex.GetBaseException().Message;
        if (message.Contains("password", StringComparison.OrdinalIgnoreCase))
            return "Feedback could not be saved because the database rejected the password in appsettings.json.";
        if (message.Contains("No data of the requested type", StringComparison.OrdinalIgnoreCase))
            return "Feedback could not be saved because the database host could not be reached.";
        return "Feedback could not be saved because the database returned an error.";
    }
}