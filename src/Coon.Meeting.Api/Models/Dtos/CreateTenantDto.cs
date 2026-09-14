using System.ComponentModel.DataAnnotations;

namespace Coon.Meeting.Api.Models.Dtos;

public class CreateTenantDto
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? WebhookUrl { get; set; }
    public string? WebhookSecret { get; set; }
    public List<string> AllowedOrigins { get; set; } = new();

    /// <summary>Defaults to a test key - a live key is an explicit, deliberate choice, not a default.</summary>
    public bool Live { get; set; } = false;
}
