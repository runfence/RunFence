irm https://claude.ai/install.ps1 | iex

$binDir = "$env:USERPROFILE\.local\bin"
$userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
if ($userPath -notlike "*$binDir*") {
    [Environment]::SetEnvironmentVariable('PATH', $userPath + ';' + $binDir, 'User')
    Write-Host "Added $binDir to user PATH"
}
if (-not (Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
}

$npmBin = "$env:APPDATA\npm"
$userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
if ($userPath -notlike "*$npmBin*") {
    [Environment]::SetEnvironmentVariable('PATH', $userPath + ';' + $npmBin, 'User')
    Write-Host "Added $npmBin to user PATH"
}

# Install codex
Write-Host "Installing @openai/codex..."
npm i -g @openai/codex

# Install jq
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'amd64' }
Write-Host "Fetching latest jq release for $arch..."
$rel = Invoke-RestMethod 'https://api.github.com/repos/jqlang/jq/releases/latest'
$asset = $rel.assets | Where-Object { $_.name -eq "jq-windows-$arch.exe" } | Select-Object -First 1
if (-not $asset) { $asset = $rel.assets | Where-Object { $_.name -like 'jq-win*.exe' } | Select-Object -First 1 }
Write-Host "Downloading $($asset.name)..."
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile "$binDir\jq.exe" -UseBasicParsing
& "$binDir\jq.exe" --version

# Install ripgrep
$rgArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'aarch64' } else { 'x86_64' }
Write-Host "Fetching latest ripgrep release for $rgArch..."
$rgRel = Invoke-RestMethod 'https://api.github.com/repos/BurntSushi/ripgrep/releases/latest'
$rgAsset = $rgRel.assets | Where-Object { $_.name -like "*$rgArch-pc-windows-msvc.zip" } | Select-Object -First 1
$rgZip = "$env:TEMP\rg.zip"
Write-Host "Downloading $($rgAsset.name)..."
Invoke-WebRequest -Uri $rgAsset.browser_download_url -OutFile $rgZip -UseBasicParsing
$rgExtract = "$env:TEMP\rg_extract"
Expand-Archive -Path $rgZip -DestinationPath $rgExtract -Force
$rgExe = Get-ChildItem -Path $rgExtract -Recurse -Filter 'rg.exe' | Select-Object -First 1
Copy-Item $rgExe.FullName "$binDir\rg.exe" -Force
Remove-Item $rgZip, $rgExtract -Recurse -Force
& "$binDir\rg.exe" --version

Write-Host 'Claude Code + codex + jq + rg installation complete!'
