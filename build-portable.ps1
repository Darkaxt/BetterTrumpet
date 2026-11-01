param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [string]$StagingDirectory = (Join-Path $PSScriptRoot 'dist\BetterTrumpet-3.4.2-portable')
)
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'Build\Release'
$dst = $StagingDirectory
$zipPath = Join-Path $OutputDirectory 'BetterTrumpet-3.4.2-portable.zip'
if ((Test-Path -LiteralPath $dst) -or (Test-Path -LiteralPath $zipPath)) {
    throw 'Output exists; use fresh paths or review existing generated output for cleanup.'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
New-Item -ItemType Directory -Path $dst | Out-Null

# Copy main files (exclude pdb, xml doc files, Windows.winmd)
Get-ChildItem $src -File | Where-Object { $_.Extension -notin '.pdb','.xml' -and $_.Name -ne 'Windows.winmd' } | Copy-Item -Destination $dst

# Copy language folders. Exclude packaging/publish output folders that may be
# produced by the Microsoft Store build under Build\Release.
Get-ChildItem $src -Directory | Where-Object { Get-ChildItem $_.FullName -File -Filter '*.resources.dll' -ErrorAction SilentlyContinue } | ForEach-Object {
    Copy-Item $_.FullName -Destination (Join-Path $dst $_.Name) -Recurse
}

# Copy WebView2 content folders (settings UI + announcements page)
foreach ($folder in @('SettingsWeb', 'AnnouncementsWeb', 'runtimes')) {
    $srcFolder = Join-Path $src $folder
    if (Test-Path $srcFolder) {
        Copy-Item $srcFolder -Destination (Join-Path $dst $folder) -Recurse
    }
}

# Create portable marker file
Set-Content (Join-Path $dst 'portable.marker') 'BetterTrumpet Portable Mode'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $dst
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\RELEASE-3.4.2.md') -Destination $dst

# Zip it
Compress-Archive -Path "$dst\*" -DestinationPath $zipPath -CompressionLevel Optimal

$size = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "ZIP created: $zipPath ($size MB)"
