using System.ComponentModel.DataAnnotations;

namespace Coon.Meeting.Api.Models.Dtos;

public class CapabilityChangeRequestDto
{
    /// <summary>The external id of whoever is asking to grant/revoke a capability - checked
    /// against the meeting's own CreatedByExternalId. Only the organizer may do this.</summary>
    [Required]
    public string RequestedByExternalId { get; set; } = string.Empty;

    /// <summary>Must be "screenShare" or "record" - anything else is a 400.</summary>
    [Required]
    public string Capability { get; set; } = string.Empty;

    public bool Allowed { get; set; }
}
