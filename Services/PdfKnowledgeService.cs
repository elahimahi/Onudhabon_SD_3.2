using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Onudhabon.Data;
using Onudhabon.Models;
using UglyToad.PdfPig;

namespace Onudhabon.Services
{
    public class PdfKnowledgeService : IPdfKnowledgeService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<PdfKnowledgeService> _logger;

        public PdfKnowledgeService(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            ApplicationDbContext dbContext,
            ILogger<PdfKnowledgeService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _dbContext = dbContext;
            _logger = logger;
        }

        public string ExtractTextFromStream(Stream stream, int maxPages = 30)
        {
            try
            {
                using var document = PdfDocument.Open(stream);
                var sb = new StringBuilder();

                var pages = document.GetPages().Take(maxPages);
                foreach (var page in pages)
                {
                    var pageText = page.Text;
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        sb.AppendLine($"[Page {page.Number}]");
                        sb.AppendLine(pageText.Trim());
                        sb.AppendLine();
                    }
                }

                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract text from PDF stream.");
                return string.Empty;
            }
        }

        public IReadOnlyList<PdfTextChunk> ExtractChunksFromStream(Stream stream, int maxPages = 40, int maxChunkCharacters = 2400)
        {
            try
            {
                using var document = PdfDocument.Open(stream);
                var chunks = new List<PdfTextChunk>();
                foreach (var page in document.GetPages().Take(maxPages))
                {
                    var text = page.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var pageChunks = SplitPageIntoChunks(text, maxChunkCharacters);
                    for (var index = 0; index < pageChunks.Count; index++)
                        chunks.Add(new PdfTextChunk(page.Number, index, pageChunks[index]));
                }
                return chunks;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract page-aware chunks from PDF stream.");
                return Array.Empty<PdfTextChunk>();
            }
        }

        private static List<string> SplitPageIntoChunks(string text, int maxCharacters)
        {
            var chunks = new List<string>();
            var current = new StringBuilder();
            foreach (var paragraph in Regex.Split(text, @"\r?\n\s*\r?\n").Select(part => part.Trim()).Where(part => part.Length > 0))
            {
                var remaining = paragraph;
                while (remaining.Length > 0)
                {
                    var available = maxCharacters - current.Length - (current.Length > 0 ? 2 : 0);
                    if (available <= 0)
                    {
                        chunks.Add(current.ToString());
                        current.Clear();
                        continue;
                    }
                    if (remaining.Length <= available)
                    {
                        if (current.Length > 0) current.AppendLine().AppendLine();
                        current.Append(remaining);
                        remaining = string.Empty;
                        continue;
                    }

                    var cut = remaining.LastIndexOf(' ', Math.Max(0, available - 1), available);
                    if (cut < 1) cut = available;
                    var part = remaining[..cut].Trim();
                    if (current.Length > 0 && current.Length + part.Length + 2 > maxCharacters)
                    {
                        chunks.Add(current.ToString());
                        current.Clear();
                    }
                    if (current.Length > 0) current.AppendLine().AppendLine();
                    current.Append(part);
                    remaining = remaining[cut..].TrimStart();
                    if (current.Length >= maxCharacters)
                    {
                        chunks.Add(current.ToString());
                        current.Clear();
                    }
                }
            }
            if (current.Length > 0) chunks.Add(current.ToString());
            return chunks;
        }

        public async Task<string> ExtractTextFromUrlAsync(string url, int maxPages = 30)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            var cacheKey = $"pdf_content_{url.Trim().ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out string? cachedText) && cachedText != null)
            {
                return cachedText;
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Could not fetch PDF from URL {Url}: HTTP {StatusCode}", url, response.StatusCode);
                    return string.Empty;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                var text = ExtractTextFromStream(stream, maxPages);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Cache extracted text for 6 hours
                    _cache.Set(cacheKey, text, TimeSpan.FromHours(6));
                }

                return text;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error downloading and extracting PDF from {Url}", url);
                return string.Empty;
            }
        }

        public string ExtractRelevantExcerpt(string fullText, string query, int maxChars = 35000)
        {
            if (string.IsNullOrWhiteSpace(fullText))
                return string.Empty;

            if (fullText.Length <= maxChars)
                return fullText;

            // Extract keywords from query
            var queryWords = Regex.Matches(query, @"\w{3,}")
                .Select(m => m.Value.ToLowerInvariant())
                .Distinct()
                .ToList();

            if (!queryWords.Any())
            {
                // Return first chunk if no specific keywords
                return fullText.Substring(0, Math.Min(fullText.Length, maxChars)) + "\n... [Content truncated]";
            }

            // Split into paragraphs / sections
            var paragraphs = fullText.Split(new[] { "\r\n\r\n", "\n\n", "[Page " }, StringSplitOptions.RemoveEmptyEntries);
            var scoredParagraphs = new List<(string text, int score, int index)>();

            for (int i = 0; i < paragraphs.Length; i++)
            {
                var p = paragraphs[i].Trim();
                if (p.Length < 20) continue;

                int score = 0;
                var lowerP = p.ToLowerInvariant();
                foreach (var kw in queryWords)
                {
                    if (lowerP.Contains(kw))
                    {
                        score += 3;
                    }
                }

                if (score > 0)
                {
                    scoredParagraphs.Add((p, score, i));
                }
            }

            if (!scoredParagraphs.Any())
            {
                return fullText.Substring(0, Math.Min(fullText.Length, maxChars)) + "\n... [Content truncated]";
            }

            var bestParagraphs = scoredParagraphs
                .OrderByDescending(sp => sp.score)
                .ThenBy(sp => sp.index)
                .Take(20)
                .OrderBy(sp => sp.index)
                .Select(sp => sp.text);

            var combined = string.Join("\n\n---\n\n", bestParagraphs);
            if (combined.Length > maxChars)
            {
                combined = combined.Substring(0, maxChars) + "\n... [Excerpt truncated for length]";
            }

            return combined;
        }

        public async Task<string> GetGroundingContextForQueryAsync(string userQuery, Guid? documentId, string ownerKey, CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();

            if (documentId.HasValue)
            {
                var document = await _dbContext.StudyDocuments
                    .AsNoTracking()
                    .Where(item => item.Id == documentId.Value && item.OwnerKey == ownerKey && item.ExpiresAt > DateTime.UtcNow)
                    .Select(item => new { item.Id, item.FileName })
                    .FirstOrDefaultAsync(cancellationToken);

                if (document == null)
                {
                    sb.AppendLine("The attached study document is unavailable or expired.");
                }
                else
                {
                    var queryWords = Regex.Matches(userQuery, @"[\p{L}\p{N}]{2,}")
                        .Select(match => match.Value.ToLowerInvariant())
                        .Distinct()
                        .Take(16)
                        .ToArray();

                    var chunks = await _dbContext.StudyDocumentChunks
                        .AsNoTracking()
                        .Where(chunk => chunk.StudyDocumentId == document.Id)
                        .Select(chunk => new { chunk.PageNumber, chunk.ChunkIndex, chunk.Content })
                        .ToListAsync(cancellationToken);

                    var selectedChunks = chunks
                        .Select(chunk => new
                        {
                            chunk.PageNumber,
                            chunk.ChunkIndex,
                            chunk.Content,
                            Score = queryWords.Count(word => chunk.Content.Contains(word, StringComparison.OrdinalIgnoreCase))
                        })
                        .Where(chunk => chunk.Score > 0)
                        .OrderByDescending(chunk => chunk.Score)
                        .ThenBy(chunk => chunk.PageNumber)
                        .ThenBy(chunk => chunk.ChunkIndex)
                        .Take(5)
                        .ToList();

                    sb.AppendLine($"=== SELECTED STUDY DOCUMENT: {document.FileName} ===");
                    if (selectedChunks.Count == 0)
                    {
                        sb.AppendLine("No matching excerpts were found in this document. Do not infer an answer from unrelated pages.");
                    }
                    else
                    {
                        foreach (var chunk in selectedChunks)
                        {
                            sb.AppendLine($"[Source: {document.FileName} — Page {chunk.PageNumber}]");
                            sb.AppendLine(chunk.Content);
                        }
                    }
                    sb.AppendLine("=== END SELECTED STUDY DOCUMENT ===");
                }
            }

            // 2. Search Relevant Platform Study Material PDFs
            try
            {
                // Cache active materials metadata for 10 minutes to avoid repeated DB queries during chat
                var materials = await _cache.GetOrCreateAsync("active_platform_materials_cache", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                    return await _dbContext.Materials
                        .Where(m => m.Status == "Active" && !string.IsNullOrEmpty(m.FileUrl))
                        .OrderByDescending(m => m.Date)
                        .Take(15)
                        .Select(m => new
                        {
                            m.Title,
                            m.Subject,
                            m.Topic,
                            m.ClassLevel,
                            m.FileUrl
                        })
                        .ToListAsync(cancellationToken);
                });

                if (materials != null && materials.Any())
                {
                    var queryLower = userQuery.ToLowerInvariant();
                    var matchingMaterials = materials
                        .Where(m =>
                            (!string.IsNullOrEmpty(m.Subject) && queryLower.Contains(m.Subject.ToLowerInvariant())) ||
                            (!string.IsNullOrEmpty(m.Topic) && queryLower.Contains(m.Topic.ToLowerInvariant())) ||
                            (!string.IsNullOrEmpty(m.Title) && queryLower.Contains(m.Title.ToLowerInvariant())) ||
                            (!string.IsNullOrEmpty(m.ClassLevel) && queryLower.Contains(m.ClassLevel.ToLowerInvariant())) ||
                            queryLower.Contains("pdf") || queryLower.Contains("material") || queryLower.Contains("note") || queryLower.Contains("study"))
                        .Take(3)
                        .ToList();

                    if (matchingMaterials.Any())
                    {
                        sb.AppendLine("=== RELEVANT PLATFORM STUDY MATERIALS ===");
                        foreach (var mat in matchingMaterials)
                        {
                            sb.AppendLine($"- Document: {mat.Title} | Subject: {mat.Subject} | Topic: {mat.Topic} | Class: {mat.ClassLevel}");
                        }

                        // Only download and extract PDF text if the user specifically requests deep content/explanation/summary
                        bool wantsDeepContent = queryLower.Contains("summarize") ||
                                               queryLower.Contains("summary") ||
                                               queryLower.Contains("explain") ||
                                               queryLower.Contains("content") ||
                                               queryLower.Contains("what does");

                        if (wantsDeepContent)
                        {
                            var bestMatch = matchingMaterials.FirstOrDefault(m => !string.IsNullOrEmpty(m.FileUrl) && m.FileUrl.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
                            if (bestMatch != null && !string.IsNullOrEmpty(bestMatch.FileUrl))
                            {
                                var pdfText = await ExtractTextFromUrlAsync(bestMatch.FileUrl, 8);
                                if (!string.IsNullOrWhiteSpace(pdfText))
                                {
                                    var excerpt = ExtractRelevantExcerpt(pdfText, userQuery, 2000);
                                    sb.AppendLine($"\nDetailed content from '{bestMatch.Title}':\n{excerpt}");
                                }
                            }
                        }

                        sb.AppendLine("=========================================");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error looking up platform study material PDFs.");
            }

            return sb.ToString();
        }
    }
}
