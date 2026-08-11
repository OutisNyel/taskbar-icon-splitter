#requires -Version 7.0

[CmdletBinding()]
param(
    [switch]$SkipRestore,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)
$nativeProject = Join-Path $projectRoot 'native\TaskbarIconSplitter.Native\TaskbarIconSplitter.Native.csproj'
$nativeTests = Join-Path $projectRoot 'native\TaskbarIconSplitter.Native.Tests\TaskbarIconSplitter.Native.Tests.csproj'
$nativeOutput = Join-Path $projectRoot 'dist\native'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(ValueFromRemainingArguments)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath' failed with exit code $LASTEXITCODE."
    }
}

function Assert-Tool {
    param([Parameter(Mandatory)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command was not found on PATH: $Name"
    }
}

Push-Location $projectRoot
try {
    Assert-Tool 'node'
    Assert-Tool 'npm'
    Assert-Tool 'dotnet'

    $sdkVersions = @(& dotnet --list-sdks)
    if (-not ($sdkVersions | Where-Object { $_ -match '^8\.' })) {
        throw @'
.NET 8 SDK is not visible in this shell. Run build.ps1 from a Visual Studio
Developer PowerShell or another pwsh session where `dotnet --list-sdks`
includes an 8.x SDK.
'@
    }

    if (-not $SkipRestore) {
        Invoke-Checked npm ci
        Invoke-Checked dotnet restore $nativeTests
        # Restore the RID last because publish --no-restore consumes this asset file.
        Invoke-Checked dotnet restore $nativeProject --runtime win-x64
    }

    Invoke-Checked npm run typecheck
    if (-not $SkipTests) {
        Invoke-Checked npm run test:extension
        Invoke-Checked dotnet run --project $nativeTests --configuration Release --no-restore
    }
    Invoke-Checked npm run build:extension

    if (Test-Path -LiteralPath $nativeOutput) {
        $resolvedOutput = [IO.Path]::GetFullPath($nativeOutput)
        $resolvedDist = [IO.Path]::GetFullPath(
            (Join-Path $projectRoot 'dist')
        ).TrimEnd('\') + '\'
        if (-not $resolvedOutput.StartsWith(
                $resolvedDist,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean unexpected output path: $resolvedOutput"
        }
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }

    Invoke-Checked dotnet publish $nativeProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $nativeOutput

    $nativeExe = Join-Path $nativeOutput 'TaskbarIconSplitter.Native.exe'
    if (-not (Test-Path -LiteralPath $nativeExe -PathType Leaf)) {
        throw "Native Host output is missing: $nativeExe"
    }

    Write-Host ''
    Write-Host 'Build completed.'
    Write-Host "Edge extension: $(Join-Path $projectRoot 'dist\extension')"
    Write-Host "Native Host:    $nativeExe"
}
finally {
    Pop-Location
}
