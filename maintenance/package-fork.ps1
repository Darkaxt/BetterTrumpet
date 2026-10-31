param(
    [Parameter(Mandatory)][string]$InnoCompiler,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ScratchDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$env:TEMP = $ScratchDirectory
$env:TMP = $ScratchDirectory
New-Item -ItemType Directory -Force -Path $OutputDirectory, $ScratchDirectory | Out-Null
$exe = Join-Path $repo 'Build\Release\BetterTrumpet.exe'
if ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -ne '3.4.1') {
    throw 'Expected completed Release build 3.4.1.'
}
Push-Location $repo
try {
    & $InnoCompiler "/O$OutputDirectory" installer.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    & ./build-portable.ps1 -OutputDirectory $OutputDirectory -StagingDirectory (Join-Path $ScratchDirectory 'portable')
    $names = @('BetterTrumpet-3.4.1-setup.exe', 'BetterTrumpet-3.4.1-portable.zip')
    $lines = foreach ($name in $names) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $name"
    }
    $lines | Set-Content -LiteralPath (Join-Path $OutputDirectory 'release-checksums-3.4.1.txt') -Encoding utf8
} finally { Pop-Location }
