# BetterTrumpet 3.4.1 (Darkaxt fork)

Based on upstream 3.4.0. Fixes the tray playback animation overwriting the muted
speaker icon while an application continues playing.

- Muting the default output stops animation and immediately restores the cross.
- Playback beginning while muted and startup while muted keep the static icon.
- Unmuting resumes animation if playback continues; paused media stays static.
- Output-device changes and legacy-icon switching obey the same rules.
- Device icon-state notifications reach the tray after mute/unmute state updates.

Updates in this fork are checked against Darkaxt/BetterTrumpet releases.
Existing user settings and installation paths are retained. Like upstream 3.4.0,
these packages are unsigned. SHA-256 checksums accompany the assets.

Use the setup executable to upgrade an existing installation. Use the portable ZIP
for a separate installation with its own config directory.

## Build and verification

Requires .NET 8 SDK, Node.js and Inno Setup 6.

```powershell
pwsh -NoProfile -File maintenance/build-fork.ps1 -ScratchDirectory D:/Temp/bettertrumpet-build
pwsh -NoProfile -File maintenance/package-fork.ps1 -InnoCompiler <path-to-ISCC.exe> -OutputDirectory <release-directory> -ScratchDirectory D:/Temp/bettertrumpet-package
```

`tests/TrayIconRegression` runs the real tray state machine and icon generator with
controlled media/device events. No host audio or user settings are changed by it.
`tests/DeviceNotificationRegression` checks the built assembly's real device view
models and their mute/unmute event ordering with a controlled Core Audio boundary.
