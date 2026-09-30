using Onudhabon.Models;

namespace Onudhabon.Services
{
    public interface IPdfKnowledgeService
    {
        string ExtractTextFromStream(Stream stream, int maxPages = 40);
        IReadOnlyList<PdfTextChunk> ExtractChunksFromStream(Stream stream, int maxPages = 40, int maxChunkCharacters = 2400);
        Task<string> ExtractTextFromUrlAsync(string url, int maxPages = 30);
        Task<string> GetGroundingContextForQueryAsync(string userQuery, Guid? documentId, string ownerKey, CancellationToken cancellationToken = default);
        string ExtractRelevantExcerpt(string fullText, string query, int maxChars = 35000);
    }
}
