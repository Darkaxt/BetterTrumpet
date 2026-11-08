# Compiled media regressions

After the complete Release build, `maintenance/build-fork.ps1` runs `hidden`.
It invokes the production popup notification handlers on the WPF dispatcher,
with intentionally absent visual controls. Hidden notifications must neither
touch those controls nor acquire the former thumbnail preload job. This is
handler regression coverage; rendered-window acceptance belongs to the native
SettingsUiRegression harness.

The optional diagnostic arguments are a subscription-refresh count and an
observation duration in seconds, followed optionally by `collect` or `weak`.
This uses the real compiled MediaSessionService and Windows SMTC manager, with
a hidden metadata-only paused session owned by the test. It plays no audio and
does not request controls on other applications. Without the third argument,
an independent native listener counts events for comparison. `collect` forces
collection between refreshes; `weak` avoids retaining observer wrappers.
The duration bounds observation only; it does not terminate an external job.
Inspect the resulting counts and whether the controlled change was published;
an empty observation does not prove registration correctness.

Native experiments did not reproduce subscription amplification, so production
subscription ownership is unchanged. The hidden-popup failure did reproduce
before the patch. Neither test establishes a reduction in overall machine CPU.

After upstream 3.5.0, `hidden` also checks real compiled pinned track/timeline
publication on the dispatcher: each queued event publishes directly, while a
session change before dispatch rejects the stale event. The session identity is
a controlled uninitialized projection with finalization suppressed; it is not a
native Windows media-session reproduction and invokes no native factory or audio.
The optional native diagnostic now pins its owned metadata-only session. Compare
its production count with that owned provider's native count, not other apps'
aggregate callbacks, since upstream routes popup content to the pinned provider.
