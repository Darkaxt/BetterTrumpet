# Compiled notification and upstream lifecycle regressions

Run through maintenance/build-fork.ps1 after the entire integrated source is ready.
The test uses actual compiled production methods and view models with controlled
Core Audio/process metadata boundaries; it does not create application windows,
change host audio devices/volume/mute, or play audio.

Coverage preserves delivered mute/unmute order, removed/reset device detach,
update digest refusal and user/session CLI isolation. It also checks released
session stopped-handler removal, pending Console default endpoint arrival/removal
and same-ID replacement, and a single pending Input-priority meter dispatch while
the dispatcher is occupied. Installer busy-state tests use a private named pipe
to hold an owned child until explicitly released, then observe real success/error
exit and launch refusal through the production installer method and flyout
notifications. There are no elapsed-time completion assertions or real installers.

The helper's --installer-fixture mode serves only that owned child. Native rendered
UI and live mute testing remain unverified under the user's desktop restriction.
