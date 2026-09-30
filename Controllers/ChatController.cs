using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Onudhabon.Data;
using Onudhabon.Models;
using Onudhabon.Services;

namespace Onudhabon.Controllers;

public sealed class ChatController : Controller
{
    private const long MaxPdfBytes = 25 * 1024 * 1024;
    private static readonly HashSet<string> AllowedStudyModes = new(StringComparer.OrdinalIgnoreCase)
        { "ask", "explain", "summary", "quiz", "solve", "notes", "exam" };
    private static readonly HashSet<string> AllowedDifficulties = new(StringComparer.OrdinalIgnoreCase)
        { "basic", "intermediate", "advanced" };
    private static readonly HashSet<string> AllowedClassLevels = Enumerable.Range(1, 12)
        .Select(level => level.ToString())
        .ToHashSet(StringComparer.Ordinal);
    private readonly IChatOrchestrator _chatOrchestrator;
    private readonly IPdfKnowledgeService _pdfService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IChatOrchestrator chatOrchestrator,
        IPdfKnowledgeService pdfService,
        ApplicationDbContext dbContext,
        ILogger<ChatController> logger)
    {
        _chatOrchestrator = chatOrchestrator;
        _pdfService = pdfService;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("ai-upload")]
    [RequestSizeLimit(MaxPdfBytes + 256 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPdfBytes + 256 * 1024)]
    public async Task<IActionResult> UploadPdfForChat(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "Please select a valid PDF file." });

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "Only PDF (.pdf) documents are supported." });

        if (file.Length > MaxPdfBytes)
            return BadRequest(new { success = false, message = "PDF file size must be 25MB or less." });

        try
        {
            using var stream = file.OpenReadStream();
            if (!stream.CanSeek)
                return BadRequest(new { success = false, message = "This PDF could not be validated. Please try another file." });

            var header = new byte[Math.Min((int)stream.Length, 1024)];
            var headerBytesRead = await stream.ReadAsync(header.AsMemory(), cancellationToken);
            if (header.AsSpan(0, headerBytesRead).IndexOf("%PDF-"u8) < 0)
                return BadRequest(new { success = false, message = "The selected file is not a valid PDF document." });
            stream.Position = 0;

            var chunks = _pdfService.ExtractChunksFromStream(stream, 40);
            if (chunks.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Could not extract readable text from this PDF. Please ensure it is not password-protected or image-only scanned."
                });
            }

            var extractedText = string.Join(' ', chunks.Select(chunk => chunk.Content));
            if (extractedText.Length > 300_000)
                return BadRequest(new { success = false, message = "This PDF contains too much text to attach. Please use a shorter document." });

            var ownerKey = GetOwnerKey();
            var studyDocument = new StudyDocument
            {
                Id = Guid.NewGuid(),
                OwnerKey = ownerKey,
                FileName = GetSafeFileName(file.FileName),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(2),
                Chunks = chunks.Select(chunk => new StudyDocumentChunk
                {
                    PageNumber = chunk.PageNumber,
                    ChunkIndex = chunk.ChunkIndex,
                    Content = chunk.Content
                }).ToList()
            };

            await _dbContext.StudyDocuments.AddAsync(studyDocument, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var wordCount = extractedText.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
            return Ok(new { success = true, documentId = studyDocument.Id, fileName = studyDocument.FileName, wordCount });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process an uploaded study PDF.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "The PDF could not be processed. Please check the file and try again."
            });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("ai-chat")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> SendMessage([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Message))
            return BadRequest(new { error = "Message cannot be empty." });

        if (request.Message.Length > 4000)
            return BadRequest(new { error = "Message is too long. Please keep it under 4,000 characters." });

        if (!AllowedStudyModes.Contains(request.StudyMode) || !AllowedDifficulties.Contains(request.Difficulty))
            return BadRequest(new { error = "Please select a valid study mode and difficulty." });

        if (!string.IsNullOrEmpty(request.ClassLevel) && !AllowedClassLevels.Contains(request.ClassLevel))
            return BadRequest(new { error = "Please select a valid class level." });

        if (request.History is { Count: > 6 } ||
            (request.History?.Any(item => item is null || item.Text.Length > 5000) ?? false) ||
            (request.History?.Sum(item => item?.Text?.Length ?? 0) ?? 0) > 16000)
            return BadRequest(new { error = "Conversation history is too large. Clear the conversation and try again." });

        var reply = await _chatOrchestrator.SendMessageAsync(request, GetOwnerKey(), cancellationToken);
        return Ok(new { reply });
    }

    private string GetOwnerKey()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
            return $"user:{userId}";

        HttpContext.Session.SetString("ai_session_initialized", "true");
        var sessionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(HttpContext.Session.Id)));
        return $"session:{sessionHash}";
    }

    private static string GetSafeFileName(string? originalName)
    {
        var leafName = Path.GetFileName(originalName ?? string.Empty);
        var safeName = new string(leafName.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Study document.pdf";
        return safeName[..Math.Min(safeName.Length, 255)];
    }
}
