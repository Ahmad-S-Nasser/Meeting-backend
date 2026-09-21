# Coon.Meeting Integration Guide

**Coon.Meeting API · v1** — REST + WebSocket (SignalR) · JWT participant tokens · `sk_test_` / `sk_live_` keys · HMAC-SHA256 webhooks

Coon.Meeting is a standalone, multi-tenant meeting-and-calling service. This is the complete
reference for integrating it into your own product — provisioning, authentication, the
meetings/calling API, webhooks, the client SDK, and a worked reference implementation (the
Coon.Meeting Dashboard) that puts all of it together.

## Table of contents

- [Overview](#overview)
- [Integration architecture](#integration-architecture)
- [Getting started](#getting-started)
- [Authentication](#authentication)
- [Core concepts](#core-concepts)
- API Reference
  - [Tenants](#tenants)
  - [Meetings](#meetings)
  - [Attendees](#attendees)
  - [Participant tokens](#participant-tokens)
  - [Call credentials](#call-credentials)
  - [Moderation](#moderation)
  - [Calendar](#calendar)
- [Real-time calling & the SDK](#real-time-calling--the-sdk)
- [Webhooks](#webhooks)
- [CORS & allowed origins](#cors--allowed-origins)
- [Errors](#errors)
- [Reference implementation: the Dashboard](#reference-implementation-the-dashboard)

## Overview

*What Coon.Meeting is, and isn't.*

Coon.Meeting handles scheduling and in-app calling for meetings. It has no concept of your end
users — no login, no profile, no password. Every meeting, attendee, and call participant is
identified by an **external id** that you supply: your own user id, your own email address,
whatever your system already uses. Coon.Meeting stores it and hands it back; it never tries to
be a second source of truth for who your users are.

That single design decision is what makes it possible to integrate in an afternoon: you don't
migrate anything, you don't sync accounts, you just tell Coon.Meeting "this meeting belongs to
this external id" and "mint a call token for that external id," and it handles the rest —
scheduling, attendee lists, WebRTC signaling, TURN credentials, calendar invites, and
moderation.

**What it owns** — Meetings, attendee lists, WebRTC signaling, TURN credential minting,
calendar (.ics) generation, webhook delivery, and per-meeting access control (Private / Any,
kick / block).

**What it never owns** — User accounts, passwords, sessions, org/team structure, billing.
That's your product's job — Coon.Meeting just needs an external id to hang a meeting or a call
participant off of.

## Integration architecture

*One backend key, one browser token — never mixed.*

Every integration follows the same shape, whether it's a two-person side project or a full
product like the Dashboard covered later in this guide:

```
Browser (your frontend)                Your backend                    Coon.Meeting
─────────────────────                 ────────────                   ─────────────
        │                                    │                               │
        │   log in, load a meeting page      │                               │
        │ ──────────────────────────────────>│                               │
        │                                    │  POST /meetings (sk_...)     │
        │                                    │ ─────────────────────────────>│
        │                                    │<──────────────────────────── │
        │                                    │                               │
        │   "give me a call token"           │                               │
        │ ──────────────────────────────────>│                               │
        │                                    │  POST .../participant-tokens │
        │                                    │ ─────────────────────────────>│
        │                                    │<──────────────────────────── │
        │<────── short-lived JWT ────────────│                               │
        │                                                                    │
        │   <CallRoom participantToken=... />   — talks directly, cross-origin
        │ ──────────────────────────────────────────────────────────────────>│
```

Your backend is the only thing that ever holds the `sk_...` API key. It's the trust boundary —
Coon.Meeting believes whatever external id and name your backend asserts, so *your* backend is
where "is this really allowed?" gets decided before a token is ever minted. Your frontend never
sees the API key; it only ever receives a participant token scoped to one meeting, valid for a
few hours, and it uses that token to talk to Coon.Meeting **directly** for the actual call (SDP
negotiation, TURN credentials) — that traffic never has to round-trip through your backend.

> **Note:** This is why [CORS / allowed origins](#cors--allowed-origins) matters: your
> frontend's origin has to be on the tenant's allow-list, or the browser-to-Coon.Meeting calls
> get silently blocked.

## Getting started

*Provisioning is an operator action, not self-serve.*

There's no signup form for Coon.Meeting itself — a tenant is provisioned once, by whoever
operates the Coon.Meeting instance, using an admin-only provisioning key that never leaves
their infrastructure.

**POST** `/api/v1/tenants` — `X-Admin-Provisioning-Key`

```bash
curl -X POST https://meetings.example.com/api/v1/tenants \
  -H "X-Admin-Provisioning-Key: <ops-only shared secret>" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Acme Inc",
    "allowedOrigins": ["https://app.acme.com"],
    "webhookUrl": "https://api.acme.com/webhooks/coon-meeting",
    "webhookSecret": "<a secret you generate>",
    "live": true
  }'
```

The response includes the raw API key **exactly once**:

```json
{
  "id": "63cde35c-acae-4f44-996d-92e2bd9fcc1e",
  "name": "Acme Inc",
  "apiKey": "sk_live_EXAMPLE_KEY_NOT_REAL_0000000000"
}
```

> **Danger:** Only a SHA-256 hash of the key is ever stored. If it's lost, there's no recovery —
> provision a new tenant. Store it in your own backend's secret manager immediately.

Once you're live, your own backend can update most of that record itself — see
[`PUT /tenants/me`](#tenants) — without going back to the operator.

## Authentication

*Two schemes. They never accept each other's tokens.*

| Scheme | Who holds it | Header | Scope |
|---|---|---|---|
| `ApiKey` | Your backend, only | `Authorization: Bearer sk_...` | Everything under your tenant — meetings, attendees, minting tokens, moderation. |
| `ParticipantToken` | One browser, one call | `Authorization: Bearer eyJ...` | Exactly one meeting's call-credentials endpoint and signaling hub. Nothing else. |

A participant token is a signed JWT your backend mints on demand — it carries the meeting id,
the participant's external id, and their display name as claims, and expires a short while
after the meeting's scheduled end (or after a fixed TTL for meetings with no set duration). It
cannot list meetings, create attendees, or do anything an API key can — it only opens the door
to one specific call.

## Core concepts

*Tenants, meetings, attendees, visibility.*

### Tenants

A tenant is your organization's own slice of Coon.Meeting — one API key, one set of allowed
browser origins, one optional webhook endpoint. Everything else in this API is scoped to the
calling tenant; there's no way to see or touch another tenant's data, by design.

### Meetings & attendees

A meeting has a title, a scheduled time, an organizer (an external id + name + optional email),
and a list of attendees (each their own external id + name + optional email). Attendees aren't
accounts — they're just rows describing who's expected, used to drive calendar invites and, for
Private meetings, who's allowed in.

### Visibility

Every meeting is **Private** or **Any**:

- **Private** (the default) — only the organizer or a listed attendee can be minted a
  participant token or join the call. Anyone else gets `403`.
- **Any** — anyone holding a valid participant token can join, whether or not they're on the
  attendee list. It's your integration's job to decide who gets handed a token for one of
  these; Coon.Meeting itself places no restriction beyond a block list.

> **Warning:** A blocked external id is rejected regardless of visibility — Block exists
> specifically to close the "anyone with the link" gap for one bad actor. See
> [Moderation](#moderation).

---

## Tenants

*Reading and updating your own tenant record.*

**GET** `/api/v1/tenants/me` — `ApiKey`

Returns your tenant's own record — never the API key hash or webhook secret.

```json
{
  "id": "63cde35c-acae-4f44-996d-92e2bd9fcc1e",
  "name": "Acme Inc",
  "apiKeyPrefix": "sk_live_EXAMPLE1",
  "webhookUrl": "https://api.acme.com/webhooks/coon-meeting",
  "allowedOrigins": ["https://app.acme.com"],
  "status": "Active",
  "createdAt": "2026-01-14T09:02:11Z"
}
```

**PUT** `/api/v1/tenants/me` — `ApiKey`

Partial update — send only the fields you want to change. A field left out is untouched; an
explicit empty string clears `webhookUrl`/`webhookSecret`; an empty array clears
`allowedOrigins`.

```json
{ "allowedOrigins": ["https://app.acme.com", "https://staging.acme.com"] }
```

Key rotation and suspension aren't self-service — those stay with whoever operates the
instance.

## Meetings

*Full CRUD, scoped to your tenant.*

**GET** `/api/v1/meetings` — `ApiKey` — every meeting belonging to your tenant.

**GET** `/api/v1/meetings/{id}` — `ApiKey`

**POST** `/api/v1/meetings` — `ApiKey`

<details><summary>curl</summary>

```bash
curl -X POST https://meetings.example.com/api/v1/meetings \
  -H "Authorization: Bearer sk_live_..." -H "Content-Type: application/json" \
  -d '{
    "title": "Q3 planning",
    "scheduledAt": "2026-10-02T15:00:00Z",
    "durationMinutes": 45,
    "visibility": "Private",
    "organizer": { "externalId": "user_882", "name": "Priya Shah", "email": "priya@acme.com" },
    "attendees": [
      { "externalId": "user_119", "name": "Devon Cole", "email": "devon@acme.com" }
    ]
  }'
```

</details>

<details><summary>C#</summary>

```csharp
// Server-side only - never ships the api key to a browser.
var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/meetings")
{
    Content = JsonContent.Create(new {
        title = "Q3 planning",
        scheduledAt = DateTime.Parse("2026-10-02T15:00:00Z"),
        durationMinutes = 45,
        visibility = "Private",
        organizer = new { externalId = "user_882", name = "Priya Shah", email = "priya@acme.com" },
        attendees = new[] { new { externalId = "user_119", name = "Devon Cole", email = "devon@acme.com" } },
    }),
};
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
var response = await httpClient.SendAsync(request);
```

</details>

<details><summary>TypeScript</summary>

```typescript
// Server-side only (Node/Deno backend) - never in browser code.
const res = await fetch(`${COON_MEETING_URL}/api/v1/meetings`, {
  method: "POST",
  headers: { Authorization: `Bearer ${apiKey}`, "Content-Type": "application/json" },
  body: JSON.stringify({
    title: "Q3 planning",
    scheduledAt: "2026-10-02T15:00:00Z",
    durationMinutes: 45,
    visibility: "Private",
    organizer: { externalId: "user_882", name: "Priya Shah", email: "priya@acme.com" },
    attendees: [{ externalId: "user_119", name: "Devon Cole", email: "devon@acme.com" }],
  }),
});
```

</details>

| Field | Type | Notes |
|---|---|---|
| `title` | string | Required. |
| `scheduledAt` | datetime (UTC) | Required. |
| `durationMinutes` | int? | Drives when reminders/participant tokens expire. |
| `visibility` | `"Private"` \| `"Any"` | Defaults to `Private` if omitted. |
| `organizer` | `{externalId, name, email?}` | Required. Always allowed to join, regardless of visibility. |
| `attendees` | array | Each becomes a listed, verified attendee. |
| `meetingLink` | string? | Shown in calendar invites; opaque to Coon.Meeting. |

**PUT** `/api/v1/meetings/{id}` — `ApiKey`

Updates the meeting's own fields (attendees are managed separately — see below). Changing
`scheduledAt`/`title`/`location`/`meetingLink` re-sends the calendar invite with an incremented
sequence number, so clients update the existing entry rather than creating a duplicate.

**DELETE** `/api/v1/meetings/{id}` — `ApiKey`

Soft-cancels — the row stays queryable with `status: "Cancelled"`, and a cancellation calendar
entry goes out. There's no hard delete: an integrator building workflows against this API
should always be able to ask "what happened to this meeting."

## Attendees

*Managed independently of the meeting itself.*

**GET** `/api/v1/meetings/{meetingId}/attendees` — `ApiKey`

**POST** `/api/v1/meetings/{meetingId}/attendees` — `ApiKey`

```json
{ "externalId": "user_204", "name": "Marisol Vega", "email": "marisol@acme.com" }
```

The moment this call succeeds, that external id is a verified attendee — if the meeting is
Private, they can be minted a token immediately.

**DELETE** `/api/v1/meetings/{meetingId}/attendees/{attendeeId}` — `ApiKey`

## Participant tokens

*The one call your frontend depends on.*

**POST** `/api/v1/meetings/{meetingId}/participant-tokens` — `ApiKey`

```json
{ "participantExternalId": "user_119", "name": "Devon Cole" }
```

```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAt": "2026-10-02T16:15:00Z",
  "meetingId": "e9cfaab0-aaa3-4e33-abae-a27ec48f192b"
}
```

Coon.Meeting trusts the `participantExternalId` and `name` you send — it checks that id against
the meeting's own access rules (organizer, attendee list, or Any-visibility), but it never
verifies the person's real-world identity. That verification is exactly what your backend
already did before making this call — a session check, an org-membership check, whatever your
product requires.

> **Danger:** For a Private meeting, an id that's neither the organizer nor a listed attendee
> gets `403 {"error": "not_invited"}`. A blocked id gets `403 {"error": "blocked"}` — checked
> before visibility, so it applies to Any meetings too.

Hand this token straight to your frontend. It's meeting-scoped and short-lived — mint a fresh
one per join, don't cache it across sessions.

## Call credentials

*Called by the browser, not your backend.*

**GET** `/api/v1/meetings/{meetingId}/call-credentials` — `ParticipantToken`

Returns time-limited TURN credentials (HMAC-derived, matching the meeting's participant) so the
browser's WebRTC stack can relay media through a restrictive NAT/firewall. `coon-meeting-sdk`
calls this for you — you'll rarely call it directly.

```json
{
  "username": "1780531200:user_119",
  "credential": "kx0Fz...=",
  "urls": ["turn:turn.example.com:3478", "turns:turn.example.com:5349"],
  "ttl": 3600
}
```

## Moderation

*Organizer-only. Kick and Block are deliberately separate.*

| Action | Effect | Can they rejoin? |
|---|---|---|
| Kick | Disconnects them from the live call right now. | Yes — a fresh token works. |
| Block | Disconnects them and records the id permanently. | No, until explicitly unblocked. |

**POST** `/api/v1/meetings/{meetingId}/participants/{participantExternalId}/kick` — `ApiKey`

**POST** `/api/v1/meetings/{meetingId}/participants/{participantExternalId}/block` — `ApiKey`

**POST** `/api/v1/meetings/{meetingId}/participants/{participantExternalId}/unblock` — `ApiKey`

```json
{ "requestedByExternalId": "user_882", "reason": "wrong call" }
```

`requestedByExternalId` is checked against the meeting's own `organizer` id — anyone else gets
`403`. A block also takes effect against the real-time hub immediately: even a participant
token minted moments before the block cannot be used to (re)join, because the join itself is
re-checked live, not just at mint time.

> **Note:** A kicked or blocked participant's client receives a `Kicked` or `Blocked` push over
> the signaling connection so it can show its own "removed from call" state and tear down its
> media — see [Real-time calling](#real-time-calling--the-sdk).

## Calendar

*RFC 5545, with UTC times and correct line-folding.*

**GET** `/api/v1/meetings/{id}/calendar.ics` — `ApiKey or ParticipantToken`

An invite/cancellation email with this same .ics content, plus an HTML alternate view, goes out
automatically on create/reschedule/cancel — best-effort, and never blocks the write it's
attached to if the mail server is unreachable.

---

## Real-time calling & the SDK

*One React component. Everything else is plumbing you don't have to write.*

The fastest path into a working call UI is `coon-meeting-sdk`:

```bash
npm install github:Ahmad-S-Nasser/Meeting-frontend
```

```tsx
import { CallRoom } from "coon-meeting-sdk";

<CallRoom
  apiBaseUrl="https://meetings.example.com"
  meetingId={meetingId}
  participantToken={token}
  participantName={displayName}
  onLeave={() => navigate("/meetings/" + meetingId)}

  // Optional: only render Kick/Block controls for the meeting's organizer.
  // The SDK never calls your backend itself - it just exposes the hooks.
  isHost={isOrganizer}
  onKickParticipant={(participantId) => api.kick(meetingId, participantId)}
  onBlockParticipant={(participantId) => api.block(meetingId, participantId)}
/>
```

`CallRoom` handles local media capture, one `RTCPeerConnection` per remote participant, TURN
credential fetching, a per-participant volume slider, and mic/camera toggles. It renders
nothing about your product's identity or permissions model — `isHost` and the two callbacks are
the entire surface for wiring in your own moderation UI.

### The signaling protocol, if you're building your own UI

Everything above is a thin layer over one SignalR hub, `/hubs/meetingCall`, authenticated with
the participant token:

| Client calls | Server pushes |
|---|---|
| `JoinCall(meetingId)` | `ExistingParticipants`, `ParticipantJoined`, `AccessDenied` |
| `LeaveCall(meetingId)` | `ParticipantLeft` |
| `SendOffer` / `SendAnswer` / `SendIceCandidate` | `ReceiveOffer` / `ReceiveAnswer` / `ReceiveIceCandidate` |
| `UpdateMediaState(mic, camera)` | `MediaStateChanged` |
| `UpdateScreenShareState(isSharing)` | `ScreenShareStateChanged` |
| `SendChatMessage(text)` | `ReceiveChatMessage` |
| `UpdateRecordingState(isRecording)` | `RecordingStateChanged` |
| — | `Kicked`, `Blocked` |

Convention: whoever joins second always initiates the SDP offer to everyone already in the
room — this avoids a double-offer race with no tie-breaker needed. If you write your own client
against this hub, keep that convention or negotiation will glare.

**Screen sharing** reuses this same offer/answer path — a screen-share track is added to the
existing peer connection and renegotiated via the normal `SendOffer`/`ReceiveOffer` flow, so
`UpdateScreenShareState`/`ScreenShareStateChanged` is purely a "here's what that next track is"
notice, not a second signaling path. `coon-meeting-sdk`'s built-in `CallRoom` handles all of this
for you; only build against `UpdateScreenShareState` directly if you're writing your own client.

**In-call chat** (`SendChatMessage`/`ReceiveChatMessage`) is call-scoped and ephemeral by
design — a pure broadcast relay, nothing written to any database. There is no history endpoint:
a participant who reconnects or joins mid-call sees no messages sent before they arrived. If your
product needs persisted chat history, that's a decision to make at your own integration layer
(store what `ReceiveChatMessage` hands you), not something Coon.Meeting provides.

**Recording** is entirely client-side, by design: Coon.Meeting's calls are genuinely
peer-to-peer (see the hub's own doc comment above - audio/video never touches this server), so
there is no server-side point to capture a canonical recording from. `coon-meeting-sdk`'s
`CallRoom` instead composites whatever the *recording participant's own browser* can see/hear
(local + every connected peer's audio, and their video tiles if requested) into one file
client-side, then hands it to your app via a **required** `onRecordingAvailable` callback - your
app must persist that file to real storage; Coon.Meeting never stores or transcribes it.
`UpdateRecordingState`/`RecordingStateChanged` is the consent-notice broadcast every participant's
client uses to show "this call is being recorded," not a signal that touches any media. A real
accepted limitation of this design: the recording only lasts as long as the recording
participant's own browser tab stays open - if they leave or crash, the recording stops. That's
the direct cost of not building a dedicated recording/media server, which would be a different
kind of infrastructure project entirely.

## Webhooks

*Best-effort delivery, signed, retried on failure.*

Set `webhookUrl`/`webhookSecret` on your tenant to receive these events as they happen, instead
of polling:

- `meeting.created`
- `meeting.updated`
- `meeting.cancelled`
- `participant.joined`
- `participant.left`

Every delivery carries a signature header:

```
X-CoonMeeting-Signature: t=1780531200,v1=8f2a91e6c4...
```

The hex value is `HMAC-SHA256("{t}.{raw request body}", webhookSecret)`. Verify by recomputing
it yourself and comparing in constant time — binding the timestamp into the signed message (not
just alongside it) is what stops a captured header+body pair from being replayed later with a
different timestamp.

<details><summary>C#</summary>

```csharp
var header = Request.Headers["X-CoonMeeting-Signature"].ToString(); // "t=...,v1=..."
var parts = header.Split(',').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
var timestamp = parts["t"];
var signature = parts["v1"];

var expected = Convert.ToHexString(
    new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret))
        .ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}"))
).ToLowerInvariant();

bool valid = CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature));
```

</details>

<details><summary>TypeScript</summary>

```typescript
import { createHmac, timingSafeEqual } from "node:crypto";

const [t, v1] = header.replace("t=", "").split(",v1=");
const expected = createHmac("sha256", webhookSecret).update(`${t}.${rawBody}`).digest("hex");

const valid = timingSafeEqual(Buffer.from(expected), Buffer.from(v1));
```

</details>

A failed delivery is retried with backoff (10s / 60s / 5m / 30m / 2h / 6h) before being marked
abandoned — Coon.Meeting never blocks the write that triggered the event on your endpoint being
reachable.

## CORS & allowed origins

*Required if your frontend calls Coon.Meeting directly (it does).*

Every origin your frontend is served from needs to be in your tenant's `allowedOrigins` — set
at provisioning time, or updated any time via [`PUT /tenants/me`](#tenants). This is what lets
the browser's direct calls (call-credentials, the signaling hub) succeed cross-origin; without
it, they fail silently at the browser's CORS layer, not with an API error you can catch
server-side.

> **Warning:** Added a staging environment on a new domain? Add it to `allowedOrigins` before
> you test calling there — this is the single most common "why won't my call connect" cause.

## Errors

*Consistent shapes, worth branching on.*

| Status | Meaning |
|---|---|
| `400` | Validation failure, or minting a token for a `Cancelled` meeting. |
| `401` | Missing or invalid API key / participant token. |
| `403` | `{"error":"blocked"}` or `{"error":"not_invited"}` from a token mint; a non-organizer calling moderation. |
| `404` | Not found, or it belongs to a different tenant than the calling key. |
| `410` | An invite/guest link has expired (Dashboard-layer, not Coon.Meeting core). |

---

## Reference implementation: the Dashboard

*A real product, built on exactly the API above — nothing more.*

Coon.Meeting Dashboard is a self-serve product (signup, login, organizations, team invites)
that lets a human create and join meetings through a UI. It's deliberately built as **just
another integrator** of Coon.Meeting — its own backend holds one Coon.Meeting API key per
organization, server-side only, and calls the API exactly as described in this guide. Its own
frontend never sees that key; it only ever receives participant tokens, same as any other
integration.

### How each piece maps

| Dashboard concept | Coon.Meeting concept |
|---|---|
| An organization signs up | A tenant is provisioned automatically, server-side, at signup |
| An org member is added as a meeting attendee by email | Translated to that member's own Dashboard user id as the external id — an outside guest with no account falls back to their raw email |
| Attendees get emailed automatically at creation | A Dashboard-layer step, not part of Coon.Meeting's own `POST /meetings` call — once that call returns, the Dashboard's backend emails each attendee a working way in: a normal Dashboard link for an org member, a guest join link for anyone else |
| "Join call" button, logged-in user | `POST .../participant-tokens` with the session's user id |
| "Get shareable link" (Any meeting) | A reusable, unauthenticated join link that mints a fresh guest external id per visitor |
| "Invite someone" (Private meeting, after creation) | `POST .../attendees` first, then a one-person scoped join link — the same mechanism creation now triggers automatically per attendee |
| Organizer clicks Kick/Block on a tile | Forwards to Coon.Meeting's moderation endpoints, asserting the session's own user id as `requestedByExternalId` |

### The one access-model decision worth calling out

Early on, the Dashboard let any member of an org join any of that org's meetings — simple, and
fine for a product with no concept of Private meetings yet. Once Coon.Meeting's Visibility
model landed, that default had to go: the Dashboard now forwards Coon.Meeting's own `403`
rather than re-implementing the access check itself. **Coon.Meeting is the one source of truth
for who may join a given meeting** — a lesson worth carrying into your own integration: don't
duplicate access logic your provider already enforces, or the two will eventually disagree.

### Anonymous guests, end to end

The Dashboard's guest-join flow is the fullest illustration of the "Any" visibility model: a
visitor with no account at all hits a public page, types a name, and the Dashboard's backend —
using the org's own API key — mints them a participant token with a freshly generated external
id. Coon.Meeting never knows or cares that this "user" doesn't correspond to any real account
anywhere; it only ever sees an external id and a name, exactly as designed.

> **Note:** Known, accepted trade-off: because a fresh id is generated per visit, blocking an
> anonymous guest only blocks that one visit's id — they can reopen the link and get a new one.
> That's inherent to "anyone with a link, no account needed," not a bug to chase.

---

*Coon.Meeting API v1 · This guide covers the full surface as of the current build — tenants,
meetings, attendees, participant tokens, call credentials, moderation, calendar, webhooks, the
real-time hub, and the Dashboard reference integration.*
