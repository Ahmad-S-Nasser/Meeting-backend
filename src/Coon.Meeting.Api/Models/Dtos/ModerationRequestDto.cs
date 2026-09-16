using System.ComponentModel.DataAnnotations;

namespace Coon.Meeting.Api.Models.Dtos;

public class ModerationRequestDto
{
    /// <summary>The external id of whoever is asking to kick/block/unblock - checked against
    /// the meeting's own CreatedByExternalId. Only the organizer may moderate.</summary>
    [Required]
    public string RequestedByExternalId { get; set; } = string.Empty;

    public string? Reason { get; set; }
}
