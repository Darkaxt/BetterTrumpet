param(
    [Parameter(Mandatory)][string]$ScratchDirectory,
    [switch]$RegressionOnly
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force -Path $ScratchDirectory | Out-Null
$env:TEMP = $ScratchDirectory
$env:TMP = $ScratchDirectory
$env:npm_config_cache = Join-Path $ScratchDirectory 'npm-cache'
$env:NUGET_PACKAGES = Join-Path $ScratchDirectory 'nuget-packages'
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
    }
} finally { Pop-Location }
