# Protocol v1

Trusted LAN prototype, HTTP on TCP 8765 and WS `/ws`. No TLS in v0.2. UDP discovery uses 8764. HTTP server accepts only loopback/RFC1918 IPv4 peers; a browser's Origin must match Host. Native requests without Origin are allowed.

`GET /health` returns `{name:"Pixel Companion",version:1}`. `POST /api/pair` accepts JSON `{code:"123456"}` and returns a 64-character random hex token, or an error. Codes expire after 5 minutes and are single-use. Five failed attempts per minute are allowed globally; 8 saved devices maximum. Disk stores SHA-256 token hashes. New code and revoke-all are local tray actions.

First WebSocket frame within 5 seconds: `{type:"auth",token:"..."}`. A successful server responds `{type:"ready",version:1}`; failed/revoked credentials produce `{type:"auth_error"}`. Tokens are not placed in URLs. Max inbound frame/message size: 4096 bytes.

The server sends `state` twice per second, with timestamp in Unix milliseconds, locked/computer, `media`, sources, selectedSource, normalized endpoint volume, `mixer`, and optional error. Each mixer entry contains its stable process-based id, display name, normalized volume, mute state and whether the audio session is active. Media includes sessionId, revision, source, title, artist, status (`none`, `paused`, `playing`), position/duration in seconds, rate, artworkHash and supported controls. Position is relative to the provider's timeline start; the client interpolates from receipt time only while playing. Unknown duration disables seek.

Artwork is JPEG, at most 384 pixels on the longest side. It is sent separately only on hash changes and to newly connected clients: `{type:"artwork",hash,data:"data:image/jpeg;base64,..."}`. The client rejects an image whose hash is no longer the current state's artworkHash.

Commands: `{id,name,sessionId,revision,value?,sourceId?,mixerId?,muted?}`. Names: play, pause, toggle, previous, next, seek (seconds), volume (0..1), source (SourceAppUserModelId, or null for automatic), mixer-volume (mixerId plus 0..1 value), and mixer-mute (mixerId plus boolean muted). Before media commands the server resolves pending media changes and rejects stale sessionId/revision. Source, endpoint volume and mixer commands are independent of track identity. A mixer id is resolved only against audio sessions enumerated at command time.

Reply: `{type:"result",id,ok,error?}`. Commands are serialized on the media bridge; duplicates of the most recent 128 IDs on the same socket reuse the prior result. Limit: 12 commands per second per socket. The client never retries an uncertain command after a timeout or reconnect; a second toggle/next could affect the wrong state. Missing acknowledgement is visible to the user.

State becomes offline after 6 seconds without messages. Reconnect backoff starts at 1 second and caps at 15 seconds. The first valid snapshot restores state. Time stays local; no old command queue is flushed.

UDP request: ASCII `PIXEL_COMPANION_DISCOVER/1`. Response JSON: `{name,port,version:1}`. No credentials or playback metadata in discovery. If broadcasts are blocked, manual IPv4 connection remains available.
