#requires -Version 7.0

[CmdletBinding()]
param(
    [string]$StoreExtensionId,
    [string]$InnoCompilerPath,
    [string]$SigningCertificateThumbprint,
    [switch]$RequireSignature,
    [switch]$SkipRestore,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)
$artifactsRoot = [IO.Path]::GetFullPath(
    (Join-Path $projectRoot 'artifacts')
)
$nativeOutput = Join-Path $projectRoot 'dist\native'
$storeOutput = Join-Path $projectRoot 'dist\store-extension'
$nativeExe = Join-Path $nativeOutput 'TaskbarIconSplitter.Native.exe'
$hostName = 'com.outis.taskbariconsplitter'
$hostManifest = Join-Path $nativeOutput "$hostName.json"
$extensionManifest = Join-Path $projectRoot 'extension\manifest.json'
$installerScript = Join-Path $projectRoot 'installer\TaskbarIconSplitter.iss'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(ValueFromRemainingArguments)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath' failed with exit code $LASTEXITCODE."
    }
}

function Get-ExtensionId {
    param([Parameter(Mandatory)][string]$ManifestPath)

    $manifest = Get-Content -Raw -LiteralPath $ManifestPath |
        ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($manifest.key)) {
        throw 'Extension manifest does not contain a fixed public key.'
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

function Resolve-InnoCompiler {
    if (-not [string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
        $resolved = [IO.Path]::GetFullPath($InnoCompilerPath)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "Inno Setup compiler was not found: $resolved"
        }
        return $resolved
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    throw @'
Inno Setup 6 was not found. Install the build-only dependency with:
winget install --id JRSoftware.InnoSetup --exact
End users do not need Inno Setup, .NET, Node.js, or an SDK.
'@
}

function Remove-Artifact {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return
    }
    $resolved = [IO.Path]::GetFullPath($Path)
    $boundary = $artifactsRoot.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith(
            $boundary,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove artifact outside artifacts: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Force
}

function Sign-Binary {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Certificate
    )

    $signature = Set-AuthenticodeSignature `
        -LiteralPath $Path `
        -Certificate $Certificate `
        -HashAlgorithm SHA256 `
        -TimestampServer 'http://timestamp.digicert.com'
    if ($signature.Status -ne 'Valid') {
        throw "Signing failed for '$Path': $($signature.StatusMessage)"
    }
}

Push-Location $projectRoot
try {
    $buildParameters = @{}
    if ($SkipRestore) {
        $buildParameters.SkipRestore = $true
    }
    if ($SkipTests) {
        $buildParameters.SkipTests = $true
    }
    & (Join-Path $PSScriptRoot 'build.ps1') @buildParameters

    Invoke-Checked npm run build:store-extension

    $manifest = Get-Content -Raw -LiteralPath $extensionManifest |
        ConvertFrom-Json
    $version = [string]$manifest.version
    if ($version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
        throw "Unsupported extension version for installer: $version"
    }

    $developmentExtensionId = Get-ExtensionId $extensionManifest
    if ([string]::IsNullOrWhiteSpace($StoreExtensionId)) {
        $StoreExtensionId = $developmentExtensionId
        Write-Warning @"
No Edge Store extension ID was supplied. The Companion will allow only the
fixed development ID: $developmentExtensionId
Rebuild with -StoreExtensionId after Partner Center assigns the store ID.
"@
    }
    if ($StoreExtensionId -notmatch '^[a-p]{32}$') {
        throw "Invalid Edge extension ID: $StoreExtensionId"
    }

    $allowedOrigins = @("chrome-extension://$developmentExtensionId/")
    if ($StoreExtensionId -ne $developmentExtensionId) {
        $allowedOrigins += "chrome-extension://$StoreExtensionId/"
    }
    $nativeManifest = [ordered]@{
        name = $hostName
        description = 'Taskbar Icon Splitter native messaging host'
        path = 'native\TaskbarIconSplitter.Native.exe'
        type = 'stdio'
        allowed_origins = $allowedOrigins
    }
    $nativeManifest |
        ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $hostManifest -Encoding utf8NoBOM

    $certificate = $null
    if (-not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
        $thumbprint = $SigningCertificateThumbprint.Replace(' ', '')
        $certificate = Get-Item `
            -LiteralPath "Cert:\CurrentUser\My\$thumbprint" `
            -ErrorAction Stop
        Sign-Binary -Path $nativeExe -Certificate $certificate
    }
    elseif ($RequireSignature) {
        throw 'A signing certificate thumbprint is required for this build.'
    }

    New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
    $extensionZip = Join-Path $artifactsRoot `
        "TaskbarIconSplitter-Edge-$version.zip"
    $installerExe = Join-Path $artifactsRoot `
        'TaskbarIconSplitter-Setup-x64.exe'
    $checksums = Join-Path $artifactsRoot `
        'TaskbarIconSplitter-SHA256SUMS.txt'
    Remove-Artifact $extensionZip
    Remove-Artifact $installerExe
    Remove-Artifact $checksums

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $storeOutput,
        $extensionZip,
        [IO.Compression.CompressionLevel]::Optimal,
        $false
    )

    $innoCompiler = Resolve-InnoCompiler
    Invoke-Checked $innoCompiler "/DAppVersion=$version" $installerScript
    if (-not (Test-Path -LiteralPath $installerExe -PathType Leaf)) {
        throw "Companion installer was not produced: $installerExe"
    }

    if ($certificate) {
        Sign-Binary -Path $installerExe -Certificate $certificate
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($extensionZip)
    try {
        $entries = @($archive.Entries | ForEach-Object FullName)
        if ($entries -notcontains 'manifest.json') {
            throw 'Store ZIP does not contain manifest.json at its root.'
        }
        if ($entries | Where-Object { $_ -match '\.map$' }) {
            throw 'Store ZIP unexpectedly contains source maps.'
        }
    }
    finally {
        $archive.Dispose()
    }

    $files = @($extensionZip, $installerExe)
    $hashLines = foreach ($file in $files) {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $file
        '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), `
            (Split-Path -Leaf $file)
    }
    $hashLines | Set-Content -LiteralPath $checksums -Encoding ascii

    $nativeSignature = Get-AuthenticodeSignature -LiteralPath $nativeExe
    $installerSignature = Get-AuthenticodeSignature -LiteralPath $installerExe
    if ($RequireSignature -and (
            $nativeSignature.Status -ne 'Valid' -or
            $installerSignature.Status -ne 'Valid')) {
        throw 'Release binaries are not validly Authenticode-signed.'
    }

    Write-Host ''
    Write-Host 'Release artifacts completed.'
    Write-Host "Development extension ID: $developmentExtensionId"
    Write-Host "Edge Store extension ID:  $StoreExtensionId"
    Write-Host "Extension ZIP:             $extensionZip"
    Write-Host "Companion installer:       $installerExe"
    Write-Host "Checksums:                  $checksums"
    Write-Host "Native signature:           $($nativeSignature.Status)"
    Write-Host "Installer signature:        $($installerSignature.Status)"
}
finally {
    Pop-Location
}
