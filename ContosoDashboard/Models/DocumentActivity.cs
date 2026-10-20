using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentActivity
{
    [Key]
    public int DocumentActivityId { get; set; }

    public int? DocumentId { get; set; }

    [Required, MaxLength(255)]
    public string DocumentTitleSnapshot { get; set; } = string.Empty;

    [Required]
    public DocumentActivityType ActivityType { get; set; }

    [Required]
    public int ActorUserId { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(255)]
    public string? TargetSnapshot { get; set; }

    [ForeignKey(nameof(ActorUserId))]
    public virtual User ActorUser { get; set; } = null!;
}

public enum DocumentActivityType
{
    Uploaded,
    MetadataUpdated,
    Replaced,
    TaskAttached,
    Downloaded,
    Deleted,
    Shared,
    ShareRevoked
}
