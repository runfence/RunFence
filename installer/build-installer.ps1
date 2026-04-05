param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# 0. Ensure required WiX extensions are installed
wix extension add --global WixToolset.BootstrapperApplications.wixext/6.0.0
wix extension add --global WixToolset.Netfx.wixext/6.0.0
wix extension add --global WixToolset.UI.wixext/6.0.0

# 1. Generate License.rtf from LICENSE.md
#    WiX requires RTF; we convert Markdown to plain-text RTF so LICENSE.md is the single source of truth.
function ConvertMdToRtfLines([string]$mdPath) {
    $lines = Get-Content $mdPath -Encoding UTF8
    $rtfLines = @()
    foreach ($line in $lines) {
        $line = $line -replace '\\', '\\' -replace '\{', '\{' -replace '\}', '\}'
        $line = $line -replace '^#{1,6}\s+', ''
        $line = $line -replace '\*\*(.+?)\*\*', '$1'
        $line = $line -replace '\*(.+?)\*',   '$1'
        $line = $line -replace '^---$',        ''
        if ($line.Trim() -eq '') { $rtfLines += '\par' } else { $rtfLines += "$line \par" }
    }
    return $rtfLines
}
function ConvertTxtToRtfLines([string]$txtPath) {
    $lines = Get-Content $txtPath -Encoding UTF8
    $rtfLines = @()
    foreach ($line in $lines) {
        $line = $line -replace '\\', '\\' -replace '\{', '\{' -replace '\}', '\}'
        if ($line.Trim() -eq '') { $rtfLines += '\par' } else { $rtfLines += "$line \par" }
    }
    return $rtfLines
}
$bodyLines  = ConvertMdToRtfLines  "$root/LICENSE.md"
$bodyLines += '\par\par'
$bodyLines += ConvertTxtToRtfLines "$root/THIRD-PARTY-NOTICES.txt"
$body = $bodyLines -join "`n"
"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}{\colortbl ;}\f0\fs18 $body}" |
    Set-Content "$PSScriptRoot/License.rtf" -Encoding ASCII

# 2. Clean + Publish
# BuildSerial = days since 2024-01-01; used for WI-compatible ProductVersion (components <= 65535).
# DateStr drives InformationalVersion (shown as "Product version" in file Properties).
# Both are computed here and passed explicitly so the build doesn't depend on Directory.Build.props.
$buildSerial    = ([datetime]::UtcNow - [datetime]::new(2024, 1, 1)).Days
$dateStr        = [datetime]::UtcNow.ToString('yyyyMMdd')
$displayVersion = "1.0.$dateStr"
$productVersion = "1.0.$buildSerial"
Remove-Item "$PSScriptRoot/publish" -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem "$root/src" -Recurse -Directory -Filter "obj" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root/src/RunFence/RunFence.csproj" -c $Configuration --no-self-contained -o "$PSScriptRoot/publish" -v minimal `
    -p:Version=1.0.0 `
    -p:FileVersion=1.0.0.0 `
    -p:InformationalVersion=$displayVersion `
    -p:IncludeSourceRevisionInInformationalVersion=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Remove-Item "$PSScriptRoot/publish/*.pdb" -Force -ErrorAction SilentlyContinue

# 3. Generate ProductComponents.wxs from publish directory.
#    - Excludes runtimes/linux and runtimes/osx (Windows-only app).
#    - Preserves runtimes/win subdirectory structure so files with the same name in different
#      dirs install to different locations, avoiding duplicate-GUID errors.
#    - Truncates identifiers longer than 72 chars using a hash suffix for uniqueness.
$publishDir = (Resolve-Path "$PSScriptRoot/publish").Path

function Get-WixId([string]$prefix, [string]$relPath) {
    $raw = $prefix + ($relPath -replace '[^A-Za-z0-9]', '_')
    if ($raw.Length -le 72) { return $raw }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($relPath)
    $hash  = [System.Security.Cryptography.MD5]::Create().ComputeHash($bytes)
    $suffix = [BitConverter]::ToString($hash).Replace('-', '').Substring(0, 10)
    return $raw.Substring(0, 61) + '_' + $suffix
}

$allFiles = Get-ChildItem $publishDir -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\runtimes\\(linux|osx)[\\\/]' }

# Collect every unique relative subdirectory path (for directory declarations)
$allRelDirs = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($file in $allFiles) {
    $rel = $file.FullName.Substring($publishDir.Length + 1)
    $dir = [System.IO.Path]::GetDirectoryName($rel)
    while (-not [string]::IsNullOrEmpty($dir)) {
        [void]$allRelDirs.Add($dir)
        $dir = [System.IO.Path]::GetDirectoryName($dir)
    }
}

# Build map: parent relDir -> list of direct child relDirs
$childDirs = @{}
foreach ($relDir in $allRelDirs) {
    $parent = [System.IO.Path]::GetDirectoryName($relDir)
    if ([string]::IsNullOrEmpty($parent)) { $parent = '' }
    if (-not $childDirs.ContainsKey($parent)) { $childDirs[$parent] = [System.Collections.Generic.List[string]]::new() }
    $childDirs[$parent].Add($relDir)
}

# Emit nested <Directory> elements recursively
function Emit-Directories([string]$parentKey, [string]$indent) {
    $result = @()
    if (-not $childDirs.ContainsKey($parentKey)) { return $result }
    foreach ($relDir in ($childDirs[$parentKey] | Sort-Object)) {
        $dirName = [System.IO.Path]::GetFileName($relDir)
        $dirId   = Get-WixId 'D_' $relDir
        $result += "$indent<Directory Id=`"$dirId`" Name=`"$dirName`">"
        $result += Emit-Directories $relDir ($indent + '  ')
        $result += "$indent</Directory>"
    }
    return $result
}

$wxsLines = @()
$wxsLines += '<?xml version="1.0" encoding="UTF-8"?>'
$wxsLines += '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
$wxsLines += '  <Fragment>'
if ($allRelDirs.Count -gt 0) {
    $wxsLines += '    <DirectoryRef Id="INSTALLFOLDER">'
    $wxsLines += Emit-Directories '' '      '
    $wxsLines += '    </DirectoryRef>'
}
$wxsLines += '    <ComponentGroup Id="ProductComponents" Directory="INSTALLFOLDER">'
foreach ($file in $allFiles) {
    $relPath = $file.FullName.Substring($publishDir.Length + 1)
    $relDir  = [System.IO.Path]::GetDirectoryName($relPath)
    $dirId   = if ([string]::IsNullOrEmpty($relDir)) { 'INSTALLFOLDER' } else { Get-WixId 'D_' $relDir }
    $fileId  = Get-WixId 'F_' $relPath
    $compId  = Get-WixId 'C_' $relPath
    $wxsLines += "      <Component Id=`"$compId`" Guid=`"*`" Directory=`"$dirId`"><File Id=`"$fileId`" Source=`"$($file.FullName)`" /></Component>"
}
$wxsLines += '    </ComponentGroup>'
$wxsLines += '  </Fragment>'
$wxsLines += '</Wix>'
$wxsLines | Set-Content "$PSScriptRoot/ProductComponents.wxs" -Encoding UTF8

# 4. Build MSI
$outputName = "RunFence-Setup-$displayVersion"  # e.g. "RunFence-Setup-1.0.20260325"
New-Item -ItemType Directory "$PSScriptRoot/Output" -Force | Out-Null

wix build "$PSScriptRoot/RunFence.wxs" "$PSScriptRoot/ProductComponents.wxs" `
    -ext WixToolset.UI.wixext `
    -ext WixToolset.Netfx.wixext `
    -arch x64 `
    -b "$PSScriptRoot" `
    -d ProductVersion=$productVersion `
    -o "$PSScriptRoot/Output/$outputName.msi"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 5. Locate signtool (needed for both MSI and bundle)
$pfx = "$root/../RunFence.Private/codesign.pfx"
$signtool = $null
if (Test-Path $pfx) {
    $signtool = $env:SIGNTOOL_PATH
    if (-not $signtool) {
        # Probe NuGet global packages cache for Microsoft.Windows.SDK.BuildTools
        $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { "$env:USERPROFILE\.nuget\packages" }
        $candidate = Get-ChildItem "$nugetRoot\microsoft.windows.sdk.buildtools\*\bin\*\x64\signtool.exe" `
                         -ErrorAction SilentlyContinue |
                     Sort-Object FullName -Descending | Select-Object -First 1
        if ($candidate) { $signtool = $candidate.FullName }
    }
    if (-not $signtool) {
        # Fallback: Windows SDK installation
        $candidate = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Filter "signtool.exe" `
                         -Recurse -ErrorAction SilentlyContinue |
                     Where-Object { $_.FullName -match "\\x64\\" } |
                     Sort-Object FullName -Descending | Select-Object -First 1
        if ($candidate) { $signtool = $candidate.FullName }
    }
    if (-not $signtool) { Write-Warning "signtool.exe not found -- outputs will be unsigned." }
}

function Invoke-Sign([string]$target) {
    if (-not $signtool) { return }
    $signArgs = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/f', $pfx)
    if ($env:RUNASMGR_SIGN_PASSWORD) { $signArgs += @('/p', $env:RUNASMGR_SIGN_PASSWORD) }
    $signArgs += $target
    & $signtool @signArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# 6. Sign the MSI first — the bundle will embed this signed MSI
Invoke-Sign "$PSScriptRoot/Output/$outputName.msi"

# 7. Build bundle EXE (Burn bootstrapper: online installer — downloads .NET 10 at install time if needed)
#    The runtime is downloaded here only to compute its hash for the bundle manifest,
#    then deleted — users download it themselves at install time only if .NET 10 is absent.
$dotnetRuntime = "$PSScriptRoot/dotnet-runtime-10-win-x64.exe"
$dotnetRuntimeUrlFile = "$PSScriptRoot/dotnet-runtime-10-win-x64.url"
if ((Test-Path $dotnetRuntime) -and (Test-Path $dotnetRuntimeUrlFile)) {
    $dotnetDownloadUrl = (Get-Content $dotnetRuntimeUrlFile -Raw).Trim()
    Write-Host "Using cached .NET 10 Windows Desktop Runtime."
} else {
    Write-Host "Downloading .NET 10 Windows Desktop Runtime (~55 MB) for bundle hash computation..."
    try {
        $iwr = Invoke-WebRequest "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe" `
            -OutFile $dotnetRuntime -PassThru -UseBasicParsing
        $baseResp = $iwr.BaseResponse
        if ($baseResp -is [System.Net.HttpWebResponse]) {
            $dotnetDownloadUrl = $baseResp.ResponseUri.AbsoluteUri
        } else {
            $dotnetDownloadUrl = $baseResp.RequestMessage.RequestUri.AbsoluteUri
        }
        Set-Content $dotnetRuntimeUrlFile $dotnetDownloadUrl -Encoding UTF8
    } catch {
        Remove-Item $dotnetRuntime -Force -ErrorAction SilentlyContinue
        Remove-Item $dotnetRuntimeUrlFile -Force -ErrorAction SilentlyContinue
        throw
    }
}
$msiPath = (Resolve-Path "$PSScriptRoot/Output/$outputName.msi").Path
$dotnetRuntimePath = (Resolve-Path $dotnetRuntime).Path
wix build "$PSScriptRoot/Bundle.wxs" `
    -ext WixToolset.BootstrapperApplications.wixext `
    -ext WixToolset.Netfx.wixext `
    -b "$PSScriptRoot" `
    -d ProductVersion=$productVersion `
    -d "MsiPath=$msiPath" `
    -d "DotNet10RuntimePath=$dotnetRuntimePath" `
    -d "DotNet10DownloadUrl=$dotnetDownloadUrl" `
    -o "$PSScriptRoot/Output/$outputName.exe"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Remove-Item "$PSScriptRoot/Output/dotnet-runtime-10-win-x64.exe" -Force -ErrorAction SilentlyContinue
Remove-Item "$PSScriptRoot/Output/$outputName.msi" -Force -ErrorAction SilentlyContinue

# 8. Sign the bundle EXE
#    WiX bundles require detach/reattach of the engine around signing; signing the bundle
#    directly with signtool corrupts the attached container (WixAttachedContainer) reference.
$bundlePath = "$PSScriptRoot/Output/$outputName.exe"
if ($signtool) {
    $enginePath = "$PSScriptRoot/Output/$outputName-engine.exe"
    wix burn detach $bundlePath -engine $enginePath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Invoke-Sign $enginePath
    wix burn reattach $bundlePath -engine $enginePath -o $bundlePath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Remove-Item $enginePath -Force -ErrorAction SilentlyContinue
    Invoke-Sign $bundlePath
}

# 9. Create portable ZIP (same filtered file set as MSI -- excludes linux/osx runtimes)
Add-Type -Assembly System.IO.Compression.FileSystem
$zipPath = "$PSScriptRoot/Output/RunFence-$displayVersion.zip"
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($file in $allFiles) {
        $relPath = $file.FullName.Substring($publishDir.Length + 1)
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relPath)
    }
} finally {
    $zip.Dispose()
}

Write-Host "Done: $PSScriptRoot/Output/$outputName.exe"
Write-Host "Done: $zipPath"
