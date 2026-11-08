# BetterTrumpet 3.5.1 (Darkaxt fork)

Integrates upstream through af3aef91a08eaf715dcf7931c80376ac2a27c752.

- Includes upstream flyout motion, media-popup rendering/session selection and
  optional tick-sound selection and device callback
  release, plus the installer Norwegian translation.
- Preserves static muted tray glyphs, actual mute/unmute notification ordering,
  hidden popup work deferral and cancellation of pending cover/track work on hide.
- Publishes pinned media-track changes directly on the UI dispatcher, preserving
  actual notifications without introducing upstream's 150ms settling debounce.
- Keeps the updater pointed at Darkaxt/BetterTrumpet and existing user state.
- Completes upstream's device-view-model detach by also releasing its base
  stream subscription; removed/reset devices no longer forward notifications.
- Retains the fork's existing portable-update path. Upstream's new native applier
  is excluded because its forced shutdown deadlines, fixed retries and broad
  temporary-folder cleanup conflict with this fork's maintenance constraints.

Candidate only: full integrated and native rendered verification must pass before
publication or local deployment. Third-party package manifests are upstream source
only; this fork does not publish Chocolatey or WinGet packages.
