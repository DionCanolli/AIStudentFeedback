using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AIFeedbackSystem.Services;

public class NlpService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NlpService> _logger;
    private readonly string _apiToken;

    // Two Hugging Face models — one for sentiment, one for topic classification.
    // Calls go through the Inference Router. api-inference.huggingface.co no longer resolves.
    private const string SentimentModel = "distilbert/distilbert-base-uncased-finetuned-sst-2-english";
    private const string TopicModel = "facebook/bart-large-mnli";
    private const string InferenceRoot = "https://router.huggingface.co/hf-inference/models";

    public NlpService(HttpClient httpClient, IConfiguration config, ILogger<NlpService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiToken = config["HuggingFace:ApiToken"] ?? throw new InvalidOperationException("Missing Hugging Face API token");
    }

    public async Task<(string Sentiment, string Topic, bool Analyzed)> AnalyzeAsync(string comment)
    {
        var sentiment = await GetSentimentAsync(comment);
        var topic = await GetTopicAsync(comment);
        if (sentiment.ReachedApi && topic.ReachedApi)
            return (sentiment.Label, topic.Label, true);

        var local = EstimateLocally(comment);
        return (
            sentiment.ReachedApi ? sentiment.Label : local.Sentiment,
            topic.ReachedApi ? topic.Label : local.Topic,
            false);
    }

    // Used when the Hugging Face token cannot call Inference Providers.
    private static (string Sentiment, string Topic) EstimateLocally(string text)
    {
        var lower = text.ToLowerInvariant();
        var positive = CountWords(lower, "good", "great", "excellent", "love", "helpful", "clear", "amazing", "best", "enjoy", "wonderful", "nice", "awesome", "perfect", "thanks", "thank");
        var negative = CountWords(lower, "bad", "poor", "hate", "broken", "terrible", "awful", "worst", "boring", "confusing", "slow", "hard", "difficult", "useless", "disappointing");

        var sentiment = positive > negative ? "Positive" : negative > positive ? "Negative" : "Neutral";

        var topic = "General";
        if (CountWords(lower, "projector", "wifi", "room", "building", "lab", "computer", "facility", "internet", "classroom") > 0)
            topic = "Infrastructure";
        else if (CountWords(lower, "teacher", "professor", "lecture", "teaching", "instructor", "explain") > 0)
            topic = "Teaching Quality";
        else if (CountWords(lower, "homework", "assignment", "coursework", "exam", "quiz", "deadline", "project") > 0)
            topic = "Coursework";

        return (sentiment, topic);
    }

    private static int CountWords(string text, params string[] words)
    {
        var count = 0;
        foreach (var word in words)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b"))
                count++;
        }
        return count;
    }

    // --- REQ 3: Sends text to NLP API (sentiment model) ---
    private async Task<(string Label, bool ReachedApi)> GetSentimentAsync(string text)
    {
        var payload = JsonSerializer.Serialize(new { inputs = text });
        var json = await PostInferenceAsync(SentimentModel, payload);
        if (json is null)
            return ("Neutral", false);

        try
        {
            // The model returns both labels. The higher score is the result.
            var label = ReadBestScoredLabel(json);
            if (label != null && label.Contains("POSITIVE", StringComparison.OrdinalIgnoreCase))
                return ("Positive", true);
            if (label != null && label.Contains("NEGATIVE", StringComparison.OrdinalIgnoreCase))
                return ("Negative", true);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not read sentiment from Hugging Face.");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Could not read sentiment from Hugging Face.");
        }

        return ("Neutral", false);
    }

    // --- REQ 5: Sends text to NLP API (zero-shot classification for topics) ---
    private async Task<(string Label, bool ReachedApi)> GetTopicAsync(string text)
    {
        // We give the model a list of candidate topics and it picks the best match
        var payload = JsonSerializer.Serialize(new
        {
            inputs = text,
            parameters = new
            {
                candidate_labels = new[]
                {
                    "Infrastructure",
                    "Teaching Quality",
                    "Coursework",
                    "General"
                }
            }
        });

        var json = await PostInferenceAsync(TopicModel, payload);
        if (json is null)
            return ("General", false);

        try
        {
            var label = ReadBestScoredLabel(json);
            if (!string.IsNullOrWhiteSpace(label))
                return (label, true);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not read topic labels from Hugging Face.");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Could not read topic labels from Hugging Face.");
        }

        return ("General", false);
    }

    // Picks the label with the highest score, whether the payload is a list of
    // {label, score} objects or {labels, scores} from zero-shot classification.
    private static string? ReadBestScoredLabel(string json)
    {
        using var doc = JsonDocument.Parse(json);
        string? best = null;
        var bestScore = double.NegativeInfinity;
        Consider(doc.RootElement, ref best, ref bestScore);
        return best;

        static void Consider(JsonElement element, ref string? best, ref double bestScore)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        Consider(item, ref best, ref bestScore);
                    break;
                case JsonValueKind.Object:
                    if (element.TryGetProperty("label", out var labelElement)
                        && labelElement.ValueKind == JsonValueKind.String)
                    {
                        var score = 0d;
                        if (element.TryGetProperty("score", out var scoreElement) && scoreElement.TryGetDouble(out var parsed))
                            score = parsed;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = labelElement.GetString();
                        }
                    }

                    if (element.TryGetProperty("labels", out var labels)
                        && labels.ValueKind == JsonValueKind.Array
                        && labels.GetArrayLength() > 0)
                    {
                        var score = 1d;
                        if (element.TryGetProperty("scores", out var scores)
                            && scores.ValueKind == JsonValueKind.Array
                            && scores.GetArrayLength() > 0
                            && scores[0].TryGetDouble(out var topScore))
                            score = topScore;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = labels[0].GetString();
                        }
                    }
                    break;
            }
        }
    }

    private async Task<string?> PostInferenceAsync(string model, string payload)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{InferenceRoot}/{model}")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);

                using var response = await _httpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                // A cold model answers 503 while it loads. Wait once, then retry.
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt == 0)
                {
                    var seconds = 5d;
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object
                            && doc.RootElement.TryGetProperty("estimated_time", out var eta)
                            && eta.TryGetDouble(out var parsed))
                            seconds = Math.Clamp(parsed, 1, 20);
                    }
                    catch (JsonException)
                    {
                    }

                    _logger.LogInformation("Hugging Face model {Model} is loading. Retrying in {Seconds}s.", model, seconds);
                    await Task.Delay(TimeSpan.FromSeconds(seconds));
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var detail = body.Length > 300 ? body[..300] : body;
                    _logger.LogWarning("Hugging Face model {Model} returned {StatusCode}. {Detail}", model, (int)response.StatusCode, detail);
                    return null;
                }

                return body;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Could not reach Hugging Face model {Model}.", model);
                return null;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Hugging Face request for {Model} timed out.", model);
                return null;
            }
        }

        return null;
    }
}