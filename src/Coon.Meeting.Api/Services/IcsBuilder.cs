using System.Globalization;
using System.Text;
using Coon.Meeting.Api.Models;

namespace Coon.Meeting.Api.Services;

/// <summary>How a calendar object is meant to be treated by the receiving client.</summary>
public static class IcsMethods
{
    /// <summary>A new or updated invitation the recipient can accept.</summary>
    public const string Request = "REQUEST";

    /// <summary>The meeting is off. Same UID, higher sequence.</summary>
    public const string Cancel = "CANCEL";

    /// <summary>Read-only feed. No RSVP, no organizer actions.</summary>
    public const string Publish = "PUBLISH";
}

public interface IIcsBuilder
{
    string Build(Models.Meeting meeting, string method, int sequence = 0);
}

/// <summary>
/// Generates RFC 5545 VCALENDAR text for a meeting. Ported near-verbatim from SquadSpace's
/// IcsBuilder - it was already generic, so only the field names below (DurationMinutes vs
/// Duration, CreatedByName/Email vs a Contributor navigation property) change.
/// </summary>
/// <remarks>
/// Three details that decide whether an invite works or merely looks like it does:
///
/// - **UID is stable across the meeting's life.** Rescheduling sends the same UID with a
///   higher SEQUENCE, so a client updates the existing entry. A fresh UID each time produces a
///   duplicate in the attendee's calendar and no way to cancel the first one.
/// - **Times are emitted in UTC** (the trailing Z form), with the organizer's zone carried in
///   X-WR-TIMEZONE for display. Emitting a local time without a full VTIMEZONE block - which
///   would mean shipping the entire IANA database - is the classic way to be an hour out for
///   half the year.
/// - **Lines are folded at 75 octets.** A long description otherwise produces a file some
///   clients silently refuse, and the failure looks like "the invite just didn't arrive".
/// </remarks>
public class IcsBuilder : IIcsBuilder
{
    /// <summary>Namespaces the UID so it cannot collide with another system's events.</summary>
    private const string UidDomain = "coon-meeting";

    public string Build(Models.Meeting meeting, string method, int sequence = 0)
    {
        if (meeting == null) return string.Empty;

        var start = DateTime.SpecifyKind(meeting.ScheduledAt, DateTimeKind.Utc);
        var end = start.AddMinutes(meeting.DurationMinutes is > 0 ? meeting.DurationMinutes.Value : 30);

        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Coon.Meeting//Meetings//EN",
            "CALSCALE:GREGORIAN",
            $"METHOD:{method}",
        };

        // Display hint only. The timestamps below are absolute UTC, so a client that ignores
        // this still lands on the right instant.
        if (!string.IsNullOrWhiteSpace(meeting.TimeZone))
        {
            lines.Add($"X-WR-TIMEZONE:{Escape(meeting.TimeZone)}");
        }

        lines.Add("BEGIN:VEVENT");
        lines.Add($"UID:{meeting.Id}@{UidDomain}");
        lines.Add($"DTSTAMP:{Stamp(DateTime.UtcNow)}");
        lines.Add($"DTSTART:{Stamp(start)}");
        lines.Add($"DTEND:{Stamp(end)}");
        lines.Add($"SEQUENCE:{sequence}");
        lines.Add($"SUMMARY:{Escape(meeting.Title)}");

        var description = BuildDescription(meeting);
        if (!string.IsNullOrWhiteSpace(description))
        {
            lines.Add($"DESCRIPTION:{Escape(description)}");
        }

        // A joining link is more useful as the location than a room name nobody can click, so
        // it wins when both are present.
        var location = !string.IsNullOrWhiteSpace(meeting.MeetingLink)
            ? meeting.MeetingLink
            : meeting.Location;

        if (!string.IsNullOrWhiteSpace(location))
        {
            lines.Add($"LOCATION:{Escape(location)}");
        }

        if (!string.IsNullOrWhiteSpace(meeting.MeetingLink))
        {
            lines.Add($"URL:{Escape(meeting.MeetingLink)}");
        }

        if (!string.IsNullOrWhiteSpace(meeting.CreatedByEmail))
        {
            var organizerName = string.IsNullOrWhiteSpace(meeting.CreatedByName)
                ? meeting.CreatedByEmail
                : meeting.CreatedByName;

            lines.Add($"ORGANIZER;CN={Escape(organizerName)}:mailto:{meeting.CreatedByEmail}");
        }

        foreach (var attendee in meeting.Attendees ?? new List<MeetingAttendee>())
        {
            if (string.IsNullOrWhiteSpace(attendee?.Email)) continue;

            var name = string.IsNullOrWhiteSpace(attendee.Name) ? attendee.Email : attendee.Name;

            // RSVP=TRUE is what makes Accept/Decline appear. PUBLISH is a read-only feed, so
            // asking for a reply there would offer an action that goes nowhere.
            var rsvp = method == IcsMethods.Request ? ";RSVP=TRUE" : string.Empty;

            lines.Add(
                $"ATTENDEE;CN={Escape(name)};CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;" +
                $"PARTSTAT=NEEDS-ACTION{rsvp}:mailto:{attendee.Email}");
        }

        lines.Add(method == IcsMethods.Cancel ? "STATUS:CANCELLED" : "STATUS:CONFIRMED");

        if (method == IcsMethods.Request)
        {
            // Fifteen minutes is the convention most people already expect.
            lines.Add("BEGIN:VALARM");
            lines.Add("TRIGGER:-PT15M");
            lines.Add("ACTION:DISPLAY");
            lines.Add($"DESCRIPTION:{Escape(meeting.Title)}");
            lines.Add("END:VALARM");
        }

        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            // CRLF is required by the spec, not a Windows habit.
            sb.Append(Fold(line)).Append("\r\n");
        }

        return sb.ToString();
    }

    private static string BuildDescription(Models.Meeting meeting)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(meeting.Description)) parts.Add(meeting.Description);
        if (!string.IsNullOrWhiteSpace(meeting.MeetingLink)) parts.Add($"Join: {meeting.MeetingLink}");

        return string.Join("\n\n", parts);
    }

    private static string Stamp(DateTime value) =>
        value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Escapes the four characters RFC 5545 reserves inside a text value.</summary>
    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\n");
    }

    /// <summary>Folds a content line to 75 octets, continuing with a leading space.</summary>
    private static string Fold(string line)
    {
        const int limit = 75;

        var bytes = Encoding.UTF8.GetByteCount(line);
        if (bytes <= limit) return line;

        var sb = new StringBuilder();
        var current = 0;
        var first = true;

        foreach (var rune in line.EnumerateRunes())
        {
            var size = Encoding.UTF8.GetByteCount(rune.ToString());

            // Continuation lines carry a leading space, which itself costs an octet.
            var max = first ? limit : limit - 1;

            if (current + size > max)
            {
                sb.Append("\r\n ");
                current = 1;
                first = false;
            }

            sb.Append(rune.ToString());
            current += size;
        }

        return sb.ToString();
    }
}
