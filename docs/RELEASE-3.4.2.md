# BetterTrumpet 3.4.2 (Darkaxt fork)

Integrates upstream master through 1dd1726932c29537f34b6a0b2148bc5868f9ce89.

- Preserves the muted tray icon fix and correct mute/unmute notification ordering.
- Supports upstream's optional speaker, headphones and monitor tray glyphs;
  every glyph stays static while the default output is muted.
- Uses the Windows Console role for the default output, with Multimedia fallback.
- Includes upstream's safer web-link opening, per-user/session CLI pipe and
  SHA-256 verification before installing downloaded updates.
- Includes upstream settings caption controls, scroll-edge/slider polish,
  diagnostics corrections and the installer's existing-PATH removal option.

The updater continues to use Darkaxt/BetterTrumpet releases. Existing settings,
startup bindings and audio state are preserved during local upgrade. Packages are
unsigned, as in upstream; published SHA-256 checksums accompany both assets.

Build using maintenance/build-fork.ps1 and package using maintenance/package-fork.ps1.
The tray regression uses real icon/state code with controlled audio/media inputs;
the device regression exercises real compiled view-model notifications and updater
digest checks. Publication also requires settings rendering verification in an
isolated Windows guest; that check is pending an interactive guest login.
