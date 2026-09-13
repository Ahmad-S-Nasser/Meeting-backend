using System.ComponentModel.DataAnnotations;

namespace Coon.Meeting.Api.Models.Dtos;

public class AttendeeDto
{
    [Required]
    public string ExternalId { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }
}
