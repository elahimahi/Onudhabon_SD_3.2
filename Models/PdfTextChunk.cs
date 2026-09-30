namespace Onudhabon.Models;

public sealed record PdfTextChunk(int PageNumber, int ChunkIndex, string Content);
