#requires -Version 7.0

[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$hostName = 'com.outis.taskbariconsplitter'
$projectRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)
$installRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'TaskbarIconSplitter')
)
$extensionSource = Join-Path $projectRoot 'dist\extension'
$nativeSource = Join-Path $projectRoot 'dist\native'
$extensionTarget = Join-Path $installRoot 'extension'
$nativeTarget = Join-Path $installRoot 'native'
$hostManifestPath = Join-Path $installRoot "$hostName.json"
$registryKey = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"

function Get-ExtensionId {
    param([Parameter(Mandatory)][string]$ManifestPath)

    $manifest = Get-Content -Raw -LiteralPath $ManifestPath |
        ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($manifest.key)) {
        throw "Extension manifest does not contain a fixed public key."
    }

    $publicKey = [Convert]::FromBase64String($manifest.key)
    $hash = [Security.Cryptography.SHA256]::HashData($publicKey)
    $alphabet = 'abcdefghijklmnop'
    $builder = [Text.StringBuilder]::new(32)
    foreach ($value in $hash[0..15]) {
        [void]$builder.Append($alphabet[$value -shr 4])
        [void]$builder.Append($alphabet[$value -band 0x0f])
    }
    return $builder.ToString()
}

function Remove-InstallChild {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedRoot = $installRoot.TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith(
            $resolvedRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace unexpected path: $resolvedPath"
    }
    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE."
    }
}

$extensionManifest = Join-Path $extensionSource 'manifest.json'
$nativeExe = Join-Path $nativeSource 'TaskbarIconSplitter.Native.exe'
if (-not (Test-Path -LiteralPath $extensionManifest -PathType Leaf)) {
    throw "Built extension is missing. Run scripts\build.ps1 first."
}
if (-not (Test-Path -LiteralPath $nativeExe -PathType Leaf)) {
    throw "Built Native Host is missing. Run scripts\build.ps1 first."
}

New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
Remove-InstallChild $extensionTarget
Remove-InstallChild $nativeTarget
Copy-Item -LiteralPath $extensionSource -Destination $extensionTarget -Recurse
Copy-Item -LiteralPath $nativeSource -Destination $nativeTarget -Recurse

$extensionId = Get-ExtensionId(
    (Join-Path $extensionTarget 'manifest.json')
)
$installedExe = Join-Path $nativeTarget 'TaskbarIconSplitter.Native.exe'
$hostManifest = [ordered]@{
    name = $hostName
    description = 'Taskbar Icon Splitter native messaging host'
    path = $installedExe
    type = 'stdio'
    allowed_origins = @("chrome-extension://$extensionId/")
}
$hostManifest |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath $hostManifestPath -Encoding utf8NoBOM

New-Item -Path $registryKey -Force | Out-Null
Set-Item -Path $registryKey -Value $hostManifestPath

Write-Host ''
Write-Host 'Taskbar Icon Splitter installed.'
Write-Host "Extension ID: $extensionId"
Write-Host "Unpacked extension path: $extensionTarget"
Write-Host "Native Host manifest: $hostManifestPath"
Write-Host ''
Write-Host 'Open edge://extensions, enable Developer mode, then choose'
Write-Host "'Load unpacked' and select the extension path above."
