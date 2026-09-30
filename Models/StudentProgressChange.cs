using System.ComponentModel.DataAnnotations;

namespace Onudhabon.Models;

public sealed class StudentProgressChange
{
    public long Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;

    [MaxLength(40)]
    public string ActionType { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Summary { get; set; } = string.Empty;

    [MaxLength(150)]
    public string ChangedBy { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ChangedByRole { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
