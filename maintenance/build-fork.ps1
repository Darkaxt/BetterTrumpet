param(
    [Parameter(Mandatory)][string]$ScratchDirectory,
    [switch]$RegressionOnly
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
# npm adds its tool directories to PATH, which can exceed cmd.exe's 8191-character
# command-lookup limit on this host. Bind this build's actual tools explicitly;
# this child-process environment does not alter the user's persistent PATH.
$toolDirectories = foreach ($tool in @('dotnet.exe', 'node.exe', 'npm.cmd', 'git.exe', 'powershell.exe')) {
    Split-Path -Parent (Get-Command $tool -ErrorAction Stop).Source
}
$env:Path = (@($toolDirectories) + @((Join-Path $env:SystemRoot 'System32'), $env:SystemRoot) | Select-Object -Unique) -join ';'
New-Item -ItemType Directory -Force -Path $ScratchDirectory | Out-Null
$env:TEMP = $ScratchDirectory
$env:TMP = $ScratchDirectory
$env:npm_config_cache = Join-Path $ScratchDirectory 'npm-cache'
$env:NUGET_PACKAGES = Join-Path $ScratchDirectory 'nuget-packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $ScratchDirectory 'nuget-http-cache'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $repo
try {
    & dotnet run --project tests/TrayIconRegression/TrayIconRegression.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tray regression failed.' }
    if (!$RegressionOnly) {
        & dotnet build EarTrumpet/EarTrumpet.csproj -c Release -r win-x86 --self-contained -p:Platform=x86 -p:OutputPath=../Build/Release -m:2
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
        & dotnet run --project tests/DeviceNotificationRegression/DeviceNotificationRegression.csproj -c Release "-p:BaseIntermediateOutputPath=$ScratchDirectory/notification-obj/" "-p:OutputPath=$ScratchDirectory/notification-bin/" -- $ScratchDirectory
        if ($LASTEXITCODE -ne 0) { throw 'Device notification regression failed.' }
        & dotnet run --project tests/MediaSessionRegression/MediaSessionRegression.csproj -c Release "-p:BaseIntermediateOutputPath=$ScratchDirectory/media-obj/" "-p:OutputPath=$ScratchDirectory/media-bin/" -- hidden
        if ($LASTEXITCODE -ne 0) { throw 'Hidden media popup regression failed.' }
    }
} finally { Pop-Location }
