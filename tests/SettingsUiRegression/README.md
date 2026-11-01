# Native settings verification

This harness loads the built production application and its WebView2 settings
assets. It checks caption controls, supported window sizes and the real native
settings switch bridge, and writes JSON and PNG evidence beside the executable.

Build after `maintenance/build-fork.ps1`, with generated output and NuGet storage
in task-owned scratch. Run only inside an owned Coordinator Windows Sandbox:
the executable requires `BETTERTRUMPET_UI_TEST_GUEST=1` and
`C:\Coordinator\Work`. It seeds settings and theme containers only in that guest.
Never execute it on the host. An actual interactive guest login and an available
WebView2 runtime are required; a headless SYSTEM controller failure is not a
passing rendering test. The gate does not connect an interactive desktop.

Await native process completion, inspect `result.json`, both viewport JSON files
and both PNGs. An exit code or JSON record alone does not prove visual acceptance.
