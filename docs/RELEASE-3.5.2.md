# BetterTrumpet 3.5.2 (Darkaxt fork)

Integrates upstream through 4d7e725e69ccf9dd4cdc78f6ffc5614d600bd4fe.

- Releases stopped-process subscriptions when audio sessions are removed.
- Resolves the Console default output when endpoint notifications arrive out of
  order, including devices reappearing with the same ID.
- Keeps at most one pending meter update on the UI dispatcher, at Input priority,
  and detaches the device dropped by the collapsed flyout.
- Shows download/install progress in the update banner and waits for actual
  installer completion instead of resetting busy state after a fixed timeout.
- Includes upstream classic tick-sound choices, device-toast layout, narrow
  preset-row wrapping and Eco mode description corrections.
- Preserves static muted glyphs, correct mute/unmute ordering, hidden-popup work
  deferral/cancellation, direct pinned media events and completion-based reads.
- Keeps the updater on Darkaxt/BetterTrumpet and preserves the existing portable
  update path; the previously excluded native portable applier remains excluded.

Native rendered UI and live mute testing remain unperformed under the user's
explicit testing restriction. Third-party manifests are upstream source only;
this fork does not publish WinGet or Chocolatey packages.
