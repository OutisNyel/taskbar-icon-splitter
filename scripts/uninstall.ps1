#requires -Version 7.0

[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$hostName = 'com.outis.taskbariconsplitter'
$installRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'TaskbarIconSplitter')
)
$expectedRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'TaskbarIconSplitter')
)
$registryKey = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"

if (Test-Path -LiteralPath $registryKey) {
    if ($PSCmdlet.ShouldProcess($registryKey, 'Remove Native Messaging registration')) {
        Remove-Item -LiteralPath $registryKey -Recurse -Force
    }
}

if (Test-Path -LiteralPath $installRoot) {
    if (-not [string]::Equals(
            $installRoot.TrimEnd('\'),
            $expectedRoot.TrimEnd('\'),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected install directory: $installRoot"
    }
    if ($PSCmdlet.ShouldProcess($installRoot, 'Remove installed files, icons and logs')) {
        Remove-Item -LiteralPath $installRoot -Recurse -Force
    }
}

if ($WhatIfPreference) {
    Write-Host 'WhatIf only: no registry keys or files were removed.'
}
else {
    Write-Host 'Taskbar Icon Splitter Native Host registration and local files were removed.'
    Write-Host 'Remove the unpacked extension from edge://extensions if it is still listed.'
}
