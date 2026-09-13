using System.ComponentModel.DataAnnotations;

namespace Coon.Meeting.Api.Models.Dtos;

public class MintParticipantTokenDto
{
    [Required]
    public string ParticipantExternalId { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;
}
