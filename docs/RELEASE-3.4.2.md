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
- Defers media-popup title and cover work until the popup is shown, and cancels
  pending cover/track animation work when it hides. Background media events keep
  the tray state current without refreshing hidden popup content.

The updater continues to use Darkaxt/BetterTrumpet releases. Existing settings,
startup bindings and audio state are preserved during local upgrade. Packages are
unsigned, as in upstream; published SHA-256 checksums accompany both assets.

Build using maintenance/build-fork.ps1 and package using maintenance/package-fork.ps1.
The tray regression uses real icon/state code with controlled audio/media inputs;
the device regression exercises real compiled view-model notifications and updater
digest checks. Native settings rendering and the real switch bridge passed at
wide and minimum sizes in an explicitly authorized isolated desktop test.
The native media popup passed visible, hidden and reopen checks. A compiled
regression confirms hidden notifications do not start thumbnail work or update
visual content. Short native CPU comparisons had different media-event workloads
and do not establish an overall CPU reduction.
