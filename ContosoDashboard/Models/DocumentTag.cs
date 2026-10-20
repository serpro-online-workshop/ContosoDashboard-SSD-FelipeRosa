using System.ComponentModel.DataAnnotations;

namespace ContosoDashboard.Models;

public class DocumentTag
{
    public int DocumentId { get; set; }

    [Required, MaxLength(50)]
    public string Tag { get; set; } = string.Empty;

    public virtual Document Document { get; set; } = null!;
}
