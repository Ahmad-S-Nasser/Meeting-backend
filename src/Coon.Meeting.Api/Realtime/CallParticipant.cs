namespace Coon.Meeting.Api.Realtime;

/// <summary>One connection's identity within a call room, as sent to a newly-joining peer.</summary>
public class CallParticipant
{
    public string ConnectionId { get; set; } = string.Empty;
    public string ParticipantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
