param(
    [ValidateSet("Stable", "Nightly")]
    [string] $Channel = "Stable"
)

$ErrorActionPreference = "Stop"

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
$architecture = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    "X64" { "x64" }
    "Arm64" { "arm64" }
    default { throw "Unsupported architecture: $_" }
}

$archive = "razorconsole-gallery-$version-windows-$architecture.zip"
$asset = $release.assets | Where-Object name -eq $archive
$checksumsAsset = $release.assets | Where-Object name -eq "checksums-sha256.txt"
if (-not $asset -or -not $checksumsAsset) {
    throw "The latest release does not contain the expected Windows archive or checksum file."
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid())
$installRoot = if ($env:RAZORCONSOLE_GALLERY_INSTALL_DIR) {
    $env:RAZORCONSOLE_GALLERY_INSTALL_DIR
} else {
    Join-Path $env:LOCALAPPDATA "RazorConsole\Gallery"
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
    $extracted = Join-Path $temporaryDirectory "razorconsole-gallery-$version-windows-$architecture"
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

    Write-Host "Installed razorconsole-gallery $version ($($Channel.ToLowerInvariant())) to $installRoot."
    Write-Host "Open a new terminal and run: razorconsole-gallery"
}
finally {
    if (Test-Path $temporaryDirectory) {
        Remove-Item $temporaryDirectory -Recurse -Force
    }
}
