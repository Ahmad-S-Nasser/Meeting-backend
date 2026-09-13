namespace Coon.Meeting.Api.Helpers;

public static class IcsSequence
{
    /// <summary>
    /// A monotonic SEQUENCE derived from the last update - RFC 5545 needs this to increase for
    /// a client to accept a revision, and there's no counter on the model. Seconds (not minutes)
    /// since a fixed epoch: it always rises, is stable for a given meeting state, and survives a
    /// restart. Stays inside int32 until 2088.
    /// </summary>
    public static int For(Models.Meeting meeting)
    {
        var since = meeting.UpdatedAt - new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (int)Math.Max(0, Math.Min(int.MaxValue, since.TotalSeconds));
    }
}
