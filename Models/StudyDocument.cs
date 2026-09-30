using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Onudhabon.Models;

[Table("StudyDocuments")]
public sealed class StudyDocument
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(128)]
    public string OwnerKey { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    public ICollection<StudyDocumentChunk> Chunks { get; set; } = new List<StudyDocumentChunk>();
}

[Table("StudyDocumentChunks")]
public sealed class StudyDocumentChunk
{
    [Key]
    public long Id { get; set; }

    public Guid StudyDocumentId { get; set; }
    public int PageNumber { get; set; }
    public int ChunkIndex { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;

    public StudyDocument StudyDocument { get; set; } = null!;
}
