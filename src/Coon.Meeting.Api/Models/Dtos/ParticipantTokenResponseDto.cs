namespace Coon.Meeting.Api.Models.Dtos;

public class ParticipantTokenResponseDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string MeetingId { get; set; } = string.Empty;
}
