param(
    [Parameter(Mandatory)]
    [ValidateSet("Gallery", "Htop", "Snake", "TankBattle")]
    [string] $App,

    [ValidateSet("Stable", "Nightly")]
    [string] $Channel = "Stable"
)

$ErrorActionPreference = "Stop"

$appConfiguration = switch ($App) {
    "Gallery" {
        @{
            Name = "gallery"
            InstallDirectory = "Gallery"
            InstallDirectoryVariable = "RAZORCONSOLE_GALLERY_INSTALL_DIR"
        }
    }
    "Htop" {
        @{
            Name = "htop"
            InstallDirectory = "Htop"
            InstallDirectoryVariable = "RAZORCONSOLE_HTOP_INSTALL_DIR"
        }
    }
    "Snake" {
        @{
            Name = "snake"
            InstallDirectory = "Snake"
            InstallDirectoryVariable = "RAZORCONSOLE_SNAKE_INSTALL_DIR"
        }
    }
    "TankBattle" {
        @{
            Name = "tank-battle"
            InstallDirectory = "TankBattle"
            InstallDirectoryVariable = "RAZORCONSOLE_TANK_BATTLE_INSTALL_DIR"
        }
    }
}

$repository = "RazorConsole/RazorConsole"
$release = if ($Channel -eq "Stable") {
    Invoke-RestMethod "https://api.github.com/repos/$repository/releases/latest"
} else {
    $releases = Invoke-RestMethod "https://api.github.com/repos/$repository/releases?per_page=100"
    $nightly = $releases |
        Where-Object { $_.prerelease -and $_.tag_name -match '^nightly-\d{8}-\d{6}-[0-9a-f]{7}$' } |
        Sort-Object published_at -Descending |
        Select-Object -First 1
    if (-not $nightly) {
        throw "No published nightly release was found."
    }
    $nightly
}

$version = $release.tag_name -replace '^v', ''
$runtimeArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
$processorArchitecture = if ($runtimeArchitecture) {
    $runtimeArchitecture.ToString()
} elseif ($env:PROCESSOR_ARCHITEW6432) {
    $env:PROCESSOR_ARCHITEW6432
} else {
    $env:PROCESSOR_ARCHITECTURE
}
$architecture = switch ($processorArchitecture) {
    "AMD64" { "x64" }
    "X64" { "x64" }
    "ARM64" { "arm64" }
    default { throw "Unsupported architecture: $processorArchitecture" }
}

$commandName = "razorconsole-$($appConfiguration.Name)"
$archive = "$commandName-$version-windows-$architecture.zip"
$asset = $release.assets | Where-Object name -eq $archive
$checksumsAsset = $release.assets | Where-Object name -eq "checksums-sha256.txt"
if (-not $asset -or -not $checksumsAsset) {
    throw "The selected release does not contain the expected Windows archive or checksum file."
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid())
$configuredInstallRoot = [Environment]::GetEnvironmentVariable($appConfiguration.InstallDirectoryVariable)
$installRoot = if ($configuredInstallRoot) {
    $configuredInstallRoot
} else {
    Join-Path $env:LOCALAPPDATA "RazorConsole\$($appConfiguration.InstallDirectory)"
}

try {
    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
    $archivePath = Join-Path $temporaryDirectory $archive
    $checksumsPath = Join-Path $temporaryDirectory "checksums-sha256.txt"
    Invoke-WebRequest $asset.browser_download_url -OutFile $archivePath
    Invoke-WebRequest $checksumsAsset.browser_download_url -OutFile $checksumsPath

    $checksumLine = Get-Content $checksumsPath | Where-Object { $_ -match [regex]::Escape($archive) }
    if (-not $checksumLine) {
        throw "No checksum was published for $archive."
    }

    $expected = ($checksumLine -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "Checksum verification failed for $archive."
    }

    Expand-Archive $archivePath -DestinationPath $temporaryDirectory
    $extracted = Join-Path $temporaryDirectory "$commandName-$version-windows-$architecture"
    New-Item -ItemType Directory -Force $installRoot | Out-Null
    Copy-Item "$extracted\*" $installRoot -Recurse -Force

    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $pathEntries = @($userPath -split ';' | Where-Object { $_ })
    if ($installRoot -notin $pathEntries) {
        [Environment]::SetEnvironmentVariable("Path", (($pathEntries + $installRoot) -join ';'), "User")
    }
    if ($installRoot -notin ($env:Path -split ';')) {
        $env:Path = "$installRoot;$env:Path"
    }

    Write-Host "Installed $commandName $version ($($Channel.ToLowerInvariant())) to $installRoot."
    Write-Host "Open a new terminal and run: $commandName"
}
finally {
    if (Test-Path $temporaryDirectory) {
        Remove-Item $temporaryDirectory -Recurse -Force
    }
}
