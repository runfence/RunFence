Write-Host '> irm https://claude.ai/install.ps1 | iex'

function Add-ProcessPathEntry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathEntry
    )

    $pathEntries = $env:PATH.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($pathEntries -contains $PathEntry) {
        return
    }

    $env:PATH = "$PathEntry;$env:PATH"
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command,
        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE"
    }
}

function Refresh-ProcessPathFromEnvironment {
    $machinePath = [Environment]::GetEnvironmentVariable('PATH', 'Machine')
    $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
    $pathEntries = @()

    foreach ($pathValue in @($machinePath, $userPath, $env:PATH)) {
        if ([string]::IsNullOrWhiteSpace($pathValue)) {
            continue
        }

        foreach ($entry in $pathValue.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries)) {
            $trimmedEntry = $entry.Trim()
            if ([string]::IsNullOrWhiteSpace($trimmedEntry)) {
                continue
            }

            if ($pathEntries -notcontains $trimmedEntry) {
                $pathEntries += $trimmedEntry
            }
        }
    }

    $env:PATH = [string]::Join(';', $pathEntries)
}

function Find-ExecutableInPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Candidates
    )

    foreach ($candidate in $Candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path $candidate) {
            Add-ProcessPathEntry -PathEntry (Split-Path -Path $candidate -Parent)
            return $candidate
        }
    }

    return $null
}

function Get-AppPathExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutableName
    )

    foreach ($registryPath in @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\$ExecutableName",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\App Paths\$ExecutableName",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\$ExecutableName"
    )) {
        try {
            $appPath = (Get-ItemProperty -Path $registryPath -ErrorAction Stop).'(default)'
            if (-not [string]::IsNullOrWhiteSpace($appPath) -and (Test-Path $appPath)) {
                Add-ProcessPathEntry -PathEntry (Split-Path -Path $appPath -Parent)
                return $appPath
            }
        } catch {
        }
    }

    return $null
}

function Get-UninstallInstallLocation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$KeyName
    )

    foreach ($registryPath in @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$KeyName",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$KeyName",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$KeyName"
    )) {
        try {
            $installLocation = (Get-ItemProperty -Path $registryPath -ErrorAction Stop).InstallLocation
            if (-not [string]::IsNullOrWhiteSpace($installLocation) -and (Test-Path $installLocation)) {
                return $installLocation
            }
        } catch {
        }
    }

    return $null
}

function Get-GitExecutablePath {
    Refresh-ProcessPathFromEnvironment

    $gitCommand = Get-Command 'git.exe', 'git' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($gitCommand -and (Test-Path $gitCommand.Source)) {
        return $gitCommand.Source
    }

    $gitAppPath = Get-AppPathExecutable -ExecutableName 'git.exe'
    if ($gitAppPath) {
        return $gitAppPath
    }

    $candidateRoots = @(
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)},
        (Join-Path $env:LOCALAPPDATA 'Programs'),
        (Get-UninstallInstallLocation -KeyName 'Git_is1')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $candidates = foreach ($root in $candidateRoots) {
        Join-Path $root 'Git\cmd\git.exe'
        Join-Path $root 'Git\bin\git.exe'
    }

    return Find-ExecutableInPaths -Candidates $candidates
}

$gitExecutable = Get-GitExecutablePath
if (-not $gitExecutable) {
    $wingetCommand = Get-Command 'winget.exe', 'winget' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $wingetCommand) {
        throw 'git is unavailable and winget is not installed, so Git for Windows cannot be installed automatically.'
    }

    Write-Host "> & `"$($wingetCommand.Source)`" install --id Git.Git -e --source winget"
    Invoke-NativeCommand -FailureMessage 'winget failed to install Git.Git.' -Command {
        & $wingetCommand.Source install --id Git.Git -e --source winget
    }

    Refresh-ProcessPathFromEnvironment
    $gitExecutable = Get-GitExecutablePath
}

irm https://claude.ai/install.ps1 | iex

$binDir = "$env:USERPROFILE\.local\bin"
if (-not (Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
}

function Add-UserPathEntry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathEntry
    )

    $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
    if ([string]::IsNullOrWhiteSpace($userPath)) {
        [Environment]::SetEnvironmentVariable('PATH', $PathEntry, 'User')
        Write-Host "Added $PathEntry to user PATH"
        return
    }

    $pathEntries = $userPath.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($pathEntries -contains $PathEntry) {
        return
    }

    [Environment]::SetEnvironmentVariable('PATH', "$userPath;$PathEntry", 'User')
    Write-Host "Added $PathEntry to user PATH"
}

Add-UserPathEntry -PathEntry $binDir

$npmBin = "$env:APPDATA\npm"
Add-UserPathEntry -PathEntry $npmBin
Add-ProcessPathEntry -PathEntry $npmBin

Write-Host '> Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy Unrestricted -Force'
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy Unrestricted -Force

function Get-NpmExecutablePath {
    Refresh-ProcessPathFromEnvironment

    $npmCommand = Get-Command 'npm.cmd', 'npm' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($npmCommand -and (Test-Path $npmCommand.Source)) {
        return $npmCommand.Source
    }

    $candidateRoots = @(
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)},
        (Join-Path $env:LOCALAPPDATA 'Programs')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $candidates = foreach ($root in $candidateRoots) {
        Join-Path $root 'nodejs\npm.cmd'
        Join-Path $root 'nodejs\npm'
    }

    return Find-ExecutableInPaths -Candidates $candidates
}

function Wait-ForNpmExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $npmExecutable = Get-NpmExecutablePath
        if ($npmExecutable) {
            return $npmExecutable
        }

        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    return $null
}

$npmExecutable = Get-NpmExecutablePath
if (-not $npmExecutable) {
    Write-Host 'npm not found. Installing Node.js LTS via winget...'
    $wingetCommand = Get-Command 'winget.exe', 'winget' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $wingetCommand) {
        throw 'npm is unavailable and winget is not installed, so OpenJS.NodeJS.LTS cannot be installed automatically.'
    }

    Write-Host "> & `"$($wingetCommand.Source)`" install -e --id OpenJS.NodeJS.LTS"
    Invoke-NativeCommand -FailureMessage 'winget failed to install OpenJS.NodeJS.LTS.' -Command {
        & $wingetCommand.Source install -e --id OpenJS.NodeJS.LTS
    }

    Refresh-ProcessPathFromEnvironment
    Write-Host 'Waiting for npm to become available...'
    $npmExecutable = Wait-ForNpmExecutable -TimeoutSeconds 120
    if (-not $npmExecutable) {
        throw 'npm is still unavailable after installing OpenJS.NodeJS.LTS and waiting for registration to complete.'
    }
}

# Install codex
Write-Host "Installing @openai/codex..."
Write-Host "> & `"$npmExecutable`" i -g @openai/codex"
Invoke-NativeCommand -FailureMessage '@openai/codex installation failed.' -Command {
    & $npmExecutable i -g @openai/codex
}

# Install jq
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'amd64' }
Write-Host "Fetching latest jq release for $arch..."
$rel = Invoke-RestMethod 'https://api.github.com/repos/jqlang/jq/releases/latest'
$asset = $rel.assets | Where-Object { $_.name -eq "jq-windows-$arch.exe" } | Select-Object -First 1
if (-not $asset) { $asset = $rel.assets | Where-Object { $_.name -like 'jq-win*.exe' } | Select-Object -First 1 }
Write-Host "Downloading $($asset.name)..."
Write-Host "> Invoke-WebRequest -Uri `"$($asset.browser_download_url)`" -OutFile `"$binDir\\jq.exe`""
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile "$binDir\jq.exe" -UseBasicParsing
Write-Host "> & `"$binDir\\jq.exe`" --version"
Invoke-NativeCommand -FailureMessage 'jq verification failed.' -Command {
    & "$binDir\jq.exe" --version
}

# Install ripgrep
$rgArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'aarch64' } else { 'x86_64' }
Write-Host "Fetching latest ripgrep release for $rgArch..."
$rgRel = Invoke-RestMethod 'https://api.github.com/repos/BurntSushi/ripgrep/releases/latest'
$rgAsset = $rgRel.assets | Where-Object { $_.name -like "*$rgArch-pc-windows-msvc.zip" } | Select-Object -First 1
$rgZip = "$env:TEMP\rg.zip"
Write-Host "Downloading $($rgAsset.name)..."
Write-Host "> Invoke-WebRequest -Uri `"$($rgAsset.browser_download_url)`" -OutFile `"$rgZip`""
Invoke-WebRequest -Uri $rgAsset.browser_download_url -OutFile $rgZip -UseBasicParsing
$rgExtract = "$env:TEMP\rg_extract"
Write-Host "> Expand-Archive -Path `"$rgZip`" -DestinationPath `"$rgExtract`" -Force"
Expand-Archive -Path $rgZip -DestinationPath $rgExtract -Force
$rgExe = Get-ChildItem -Path $rgExtract -Recurse -Filter 'rg.exe' | Select-Object -First 1
Write-Host "> Copy-Item -Path `"$($rgExe.FullName)`" -Destination `"$binDir\\rg.exe`" -Force"
Copy-Item $rgExe.FullName "$binDir\rg.exe" -Force
Remove-Item $rgZip, $rgExtract -Recurse -Force
Write-Host "> & `"$binDir\\rg.exe`" --version"
Invoke-NativeCommand -FailureMessage 'ripgrep verification failed.' -Command {
    & "$binDir\rg.exe" --version
}

Write-Host 'Claude Code + codex + jq + rg installation complete!'
