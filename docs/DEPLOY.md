# Deploying Coon.Meeting.Api

## Required secrets

The app refuses to start (every request returns a clean `500`, no stack trace) unless these are
set as environment variables - never write real values into `appsettings.json`, which ships
with the deployment and lives in source control forever:

| Env var | Binds to | Notes |
|---|---|---|
| `Jwt__ParticipantKey` | `Jwt:ParticipantKey` | HS256 signing key for participant call tokens. Any long random string, e.g. `openssl rand -hex 32`. |
| `Turn__SharedSecret` | `Turn:SharedSecret` | Shared with your coturn server's `static-auth-secret` - see below. |

`Database__FilePath` is not a secret (just a local path) but is also overridable, for
deployments that want the LiteDB file on a mounted volume instead of `data/coon-meeting.db`
next to the binary.

## Turning on in-app calling (coturn)

Meeting calls need a TURN server so participants behind a NAT or firewall can still connect -
without one, calls only work when both sides happen to be reachable peer-to-peer. This is an
**infrastructure task, not an application deploy** - nothing here ships with the API.

1. **Generate the shared secret yourself.** `Turn:SharedSecret` is not issued by anyone - it's
   a random string you create (`openssl rand -hex 32`), used identically on both sides: as the
   `Turn__SharedSecret` environment variable on this API, and as coturn's own
   `static-auth-secret` (below). If it doesn't match on both sides, every call fails to connect.

2. **Run coturn.** A Linux container is the simplest route even on a Windows host (Docker
   Desktop / WSL2, image `coturn/coturn`). Minimal config:

   ```
   listening-port=3478
   tls-listening-port=5349
   use-auth-secret
   static-auth-secret=<same value as Turn__SharedSecret>
   realm=coon-meeting
   external-ip=<the host's public IP, or 127.0.0.1 for local-only testing>
   min-port=49152
   max-port=65535
   ```

3. **Open the firewall / security-group ports**: `3478/udp+tcp` (STUN/TURN), `5349/tcp+udp`
   (TURNS - optional, needs a TLS cert), and the relay range `49152-65535/udp` (the actual
   media-relay path - the one most often missed; narrow it in production if you want, e.g.
   `49152-49500/udp`, but it must be open).

4. **Point the API at it.** In `appsettings.json`, set `Turn:Urls` to
   `["turn:<host>:3478", "turns:<host>:5349"]` (drop the `turns:` entry if you skipped TLS).
   These are not secrets, they're public server addresses - fine to commit.

## Verifying calling end-to-end (two tabs)

There's no application UI shipped in this repo yet (the React package is a library other
frontends embed - see `frontend/README.md`), so verifying a real two-participant call means
driving the API and hub directly:

1. Run the API in Development (`dotnet run` from `src/Coon.Meeting.Api`, with
   `Jwt__ParticipantKey`/`Turn__SharedSecret` set). It prints two dev-seeded tenants' raw API
   keys to the console on first run.
2. As one tenant, create a meeting (`POST /api/v1/meetings`), then mint two participant tokens
   against it (`POST /api/v1/meetings/{id}/participant-tokens`) with two different
   `participantExternalId`s.
3. Build a minimal HTML page (or use the `frontend` package's `CallRoom` in a throwaway Vite
   app) that mounts `CallRoom` twice - once per token - each in its own browser tab, pointed at
   `apiBaseUrl` = wherever the API is running.
4. Confirm both tiles show live video/audio from the other tab. `GET /api/v1/meetings/{id}/call-credentials`
   (with a participant token) should return a real `urls`/`username`/`credential` payload once
   coturn is configured, not an empty `urls` array.
5. To confirm the TURN relay path specifically (not just LAN-local STUN), test from two
   different networks - ideally with at least one participant behind a real NAT - and check
   `chrome://webrtc-internals` for a `relay`-typed candidate pair, not just `srflx`/`host`.

This step has **not** been run against a live coturn instance as part of this build - the hub's
auth, negotiate handshake, and credential-minting HMAC were verified directly (see the Phase (b)
commit message on `Meeting-backend`), but an actual two-browser-tab audio/video join needs a
real TURN server and a browser, neither of which this environment has. Treat that as the
remaining item to close out Phase (b)'s "Done when" criterion.

## Rolling back calling

Calling has no feature flag - it's core to this product. To disable it without a full rollback,
point `Turn:Urls` at nothing and accept that calls needing TURN (most calls, in practice) won't
connect; scheduling and CRUD are unaffected either way.
