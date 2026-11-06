# Native settings verification

This harness loads the built production application and its WebView2 settings
assets. It checks caption controls, supported window sizes and the real native
settings switch bridge, and writes JSON and PNG evidence beside the executable.

Build after `maintenance/build-fork.ps1`, with generated output and NuGet storage
in task-owned scratch. Run only inside an owned Coordinator Windows Sandbox:
the executable requires `BETTERTRUMPET_UI_TEST_GUEST=1` and
`C:\Coordinator\Work`. It seeds settings and theme containers only in that guest.
The prepared host route requires a separately authorized explicit exception to
the Sandbox restriction, BETTERTRUMPET_UI_TEST_HOST=1 and the exact disposable
directory D:\Temp\bettertrumpet-performance-20261107\host-ui. It uses portable
settings and a private WebView2 profile and skips all guest registry setup. Do not
launch that route before consent. An actual interactive guest login and an available
WebView2 runtime are required; a headless SYSTEM controller failure is not a
passing rendering test. The gate does not connect an interactive desktop.

Await native process completion, inspect `result.json`, both viewport JSON files
and both PNGs. An exit code or JSON record alone does not prove visual acceptance.
The harness also verifies the real native media popup with controlled service
notifications, a hidden-state transition and fresh content on reopening. Its
playback-state changes belong only to the test process; it does not control audio
or playback in other applications.
