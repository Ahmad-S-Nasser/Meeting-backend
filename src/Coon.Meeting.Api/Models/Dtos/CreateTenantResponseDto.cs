namespace Coon.Meeting.Api.Models.Dtos;

public class CreateTenantResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Shown exactly once - only the hash is ever stored, so this can't be recovered later.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
