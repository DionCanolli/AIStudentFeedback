using AIFeedbackSystem.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AIFeedbackSystem.Pages.Dashboard;

public class IndexModel : PageModel
{
    private readonly FeedbackContext _db;
    private readonly ILogger<IndexModel> _logger;
    public IndexModel(FeedbackContext db, ILogger<IndexModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public int TotalFeedback { get; set; }
    public string? ErrorMessage { get; set; }
    public List<SentimentData> SentimentSummary { get; set; } = new();
    public List<TopicData> TopicSummary { get; set; } = new();

    public async Task OnGetAsync()
    {
        try
        {
            TotalFeedback = await _db.Feedbacks.CountAsync();

            SentimentSummary = await _db.Feedbacks
                .Where(f => f.SentimentScore != null)
                .GroupBy(f => f.SentimentScore)
                .Select(g => new SentimentData { Sentiment = g.Key!, Count = g.Count() })
                .ToListAsync();

            TopicSummary = await _db.Feedbacks
                .Where(f => f.MainTopic != null)
                .GroupBy(f => f.MainTopic)
                .Select(g => new TopicData { Topic = g.Key!, Count = g.Count() })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the dashboard.");
            var message = ex.GetBaseException().Message;
            ErrorMessage = message.Contains("password", StringComparison.OrdinalIgnoreCase)
                ? "The dashboard could not load feedback because the database rejected the password in appsettings.json."
                : "The dashboard could not load feedback because the database could not be reached.";
        }
    }

    public class SentimentData { public string Sentiment { get; set; } = ""; public int Count { get; set; } }
    public class TopicData { public string Topic { get; set; } = ""; public int Count { get; set; } }
}