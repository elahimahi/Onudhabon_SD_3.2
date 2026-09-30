using System.Text;
using System.Text.Json;
using System.Globalization;

namespace Onudhabon.Services
{
    public class GeminiChatService : ILlmChatService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string[] _fallbackModels;
        private readonly double _temperature;
        private readonly int _maxOutputTokens;
        private readonly int _timeoutSeconds;
        private readonly ILogger<GeminiChatService> _logger;

        private static string NormalizeModelName(string? model)
        {
            if (string.IsNullOrWhiteSpace(model)) return "gemini-flash-lite-latest";
            var trimmed = model.Trim();
            if (trimmed.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("models/".Length);
            }
            return trimmed;
        }

        public GeminiChatService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<GeminiChatService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            // Read from .env first, then appsettings.json
            _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                      ?? configuration["Gemini:ApiKey"]
                      ?? string.Empty;

            var configuredModel = Environment.GetEnvironmentVariable("GEMINI_MODEL")
                                  ?? configuration["Gemini:Model"];

            _model = NormalizeModelName(configuredModel);
            var configuredFallbacks = Environment.GetEnvironmentVariable("GEMINI_FALLBACK_MODELS")
                                      ?? string.Join(',', configuration.GetSection("Gemini:FallbackModels").Get<string[]>() ?? []);
            _fallbackModels = configuredFallbacks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeModelName)
                .Where(model => !string.Equals(model, _model, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray();

            _temperature = ParseDouble(Environment.GetEnvironmentVariable("GEMINI_TEMPERATURE") ?? configuration["Gemini:Temperature"], 0.2, 0, 1);
            _maxOutputTokens = ParseInt(Environment.GetEnvironmentVariable("GEMINI_MAX_OUTPUT_TOKENS") ?? configuration["Gemini:MaxOutputTokens"], 1200, 128, 4096);
            _timeoutSeconds = ParseInt(Environment.GetEnvironmentVariable("GEMINI_TIMEOUT_SECONDS") ?? configuration["Gemini:TimeoutSeconds"], 20, 5, 60);
        }

        public async Task<string> GetChatResponseAsync(string userMessage, string? systemContext = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogError("Gemini API key is not configured.");
                return "AI service is temporarily unavailable. Please try again later.";
            }

            var result = await SendGenerateContentRequestAsync(_model, userMessage, systemContext, cancellationToken);
            if (result.IsSuccess)
            {
                return result.Text;
            }

            var statusCode = result.StatusCode;
            bool shouldFallback = statusCode == System.Net.HttpStatusCode.NotFound ||
                                  statusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                                  statusCode == (System.Net.HttpStatusCode)429;

            if (shouldFallback)
            {
                foreach (var fallbackModel in _fallbackModels)
                {
                    var fallbackResult = await SendGenerateContentRequestAsync(fallbackModel, userMessage, systemContext, cancellationToken);
                    if (fallbackResult.IsSuccess)
                    {
                        return fallbackResult.Text;
                    }
                }
            }

            _logger.LogWarning("Gemini response failed. HTTP status: {StatusCode}.", result.StatusCode);
            return "AI service is temporarily unavailable. Please try again later.";
        }

        private async Task<(bool IsSuccess, string Text, string ErrorMessage, System.Net.HttpStatusCode? StatusCode)> SendGenerateContentRequestAsync(
            string modelName, string userMessage, string? systemContext, CancellationToken cancellationToken)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent";

            var requestBody = new Dictionary<string, object>();

            if (!string.IsNullOrWhiteSpace(systemContext))
            {
                requestBody["system_instruction"] = new
                {
                    parts = new[] { new { text = systemContext } }
                };
            }

            requestBody["contents"] = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userMessage } }
                }
            };

            requestBody["generationConfig"] = new
            {
                temperature = _temperature,
                maxOutputTokens = _maxOutputTokens,
                topP = 0.95
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                request.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);
                using var response = await client.SendAsync(request, cancellationToken);
                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Gemini request for model {ModelName} returned HTTP {StatusCode}.", modelName, response.StatusCode);
                    return (false, string.Empty, "AI service is temporarily unavailable. Please try again later.", response.StatusCode);
                }

                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var candidateContent) &&
                        candidateContent.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                    {
                        var text = parts[0].GetProperty("text").GetString() ?? "No response generated.";
                        return (true, text, string.Empty, response.StatusCode);
                    }
                }

                _logger.LogWarning("Gemini returned an empty response for model {ModelName}.", modelName);
                return (false, string.Empty, "AI service is temporarily unavailable. Please try again later.", response.StatusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not complete a Gemini request for model {ModelName}.", modelName);
                return (false, string.Empty, "AI service is temporarily unavailable. Please try again later.", null);
            }
        }

        private static double ParseDouble(string? value, double fallback, double min, double max) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= min && parsed <= max
                ? parsed
                : fallback;

        private static int ParseInt(string? value, int fallback, int min, int max) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= min && parsed <= max
                ? parsed
                : fallback;
    }
}
