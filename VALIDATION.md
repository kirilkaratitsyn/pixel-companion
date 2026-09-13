# Validation / v0.2

## Confirmed locally

- .NET Windows build and self-contained win-x64 publish completed without compiler warnings/errors.
- Android API 35 compilation completed; APK signature verification succeeded with one signer and v3 signing (minimum API 28).
- A previous local agent build accessed a real browser GSMTC session: title, artist, thumbnail, playback timeline, control capabilities and endpoint volume were received over WebSocket.
- A Pause command reached an independent Windows media fixture and its callback recorded `Pause`.
- v0.2 launched on the development Windows desktop, served `/health`, enumerated real Core Audio sessions for Overwatch, Chrome, Steam and system sounds, and accepted a same-level per-application volume command.
- The real HTML/CSS/JavaScript UI passed its browser contract suite in Chromium: pairing, reconnect state, play/pause, next, seek, master and per-app volume, app mute, 100% brightness, USB side selection, never-sleep default, OLED blank/wake behaviour, unsupported controls, safe rendering of hostile metadata, clock, offline mode and the 320 px layout.
- Java reports use of deprecated system UI APIs. They are retained to support Pixel 4a / Android 13; the native APK still requires a physical-device check.

## Not yet confirmed

- Full native integration suite did not complete. Initially the automatic source switched away from the paused test fixture; the test now explicitly pins its own source.
- No Pixel was attached through ADB, so installation, screen cutout, touch behaviour, local discovery and brightness on the real phone remain unverified.
- No claims of Spotify/Apple Music-specific transport compatibility, TLS protection or BLE sensor support are made for this build.

`tests/browser.cjs` exercises the real UI code against a simulated WebSocket peer. `tests/CoreChecks` verifies pairing independently. `tests/integration.cjs` requires an interactive Windows desktop with the real agent and the explicitly selected native fixture. These are different levels of evidence, not interchangeable substitutes.
