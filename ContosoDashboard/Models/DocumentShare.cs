using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentShare
{
    [Key]
    public int DocumentShareId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    public int? RecipientUserId { get; set; }

    [MaxLength(100)]
    public string? RecipientDepartment { get; set; }

    [Required]
    public int GrantedByUserId { get; set; }

    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;

    public int? RevokedByUserId { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    [ForeignKey(nameof(DocumentId))]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey(nameof(RecipientUserId))]
    public virtual User? RecipientUser { get; set; }

    [ForeignKey(nameof(GrantedByUserId))]
    public virtual User GrantedByUser { get; set; } = null!;

    [ForeignKey(nameof(RevokedByUserId))]
    public virtual User? RevokedByUser { get; set; }
}
