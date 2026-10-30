<#
.SYNOPSIS
    Checks the packed RazorConsole.Cli dotnet tool, and optionally installs it and runs it.

.DESCRIPTION
    Package checks open the .nupkg and fail, listing every problem found, unless:
      - both tools/net8.0/any and tools/net10.0/any have a DotnetToolSettings.xml for the command "razorconsole";
      - both folders contain RazorConsole.Cli.dll, RazorConsole.Core.dll and RazorConsole.Testing.dll;
      - no RazorConsole.Tests* assembly or source file is packed;
      - the nuspec is a DotnetTool with a readme and no dependency on RazorConsole.Core or RazorConsole.Testing
        (they are bundled, so a dependency would be a version-skew bug);
      - the packed readme is the tool's own, not the repository README;
      - the net8.0 runtimeconfig rolls forward to a newer major runtime, so the tool starts where only .NET 9 is installed.

    With -Install the tool is also installed into a temporary tool path from the package directory alone, then
    "razorconsole --version" must print the package version and "razorconsole doctor --json" must succeed against a
    fresh state directory. Doctor starts the real session host and a pseudoterminal session, so this runs the packed tool
    end to end. The host is stopped and the tool uninstalled at the end.

    Runs on Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER PackageDirectory
    The directory holding exactly one RazorConsole.Cli.<version>.nupkg.

.PARAMETER Install
    Also install the tool and run it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [switch]$Install
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$problems = New-Object System.Collections.Generic.List[string]

function Add-Problem([string]$message) {
    $script:problems.Add($message)
    Write-Host "  FAIL  $message"
}

function Read-Entry($archive, [string]$name) {
    $entry = $archive.Entries | Where-Object { $_.FullName -eq $name } | Select-Object -First 1
    if ($null -eq $entry) {
        return $null
    }

    $reader = New-Object System.IO.StreamReader($entry.Open())
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

# Runs a native command and returns its exit code and combined output. Native stderr must not stop the script.
function Invoke-Native([string]$file, [string[]]$arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $file @arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output.Trim() }
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

$packages = @(Get-ChildItem -Path $PackageDirectory -Filter 'RazorConsole.Cli.*.nupkg' -File |
    Where-Object { $_.Name -notlike '*.snupkg' })
if ($packages.Count -ne 1) {
    throw "Expected exactly one RazorConsole.Cli.<version>.nupkg in '$PackageDirectory', found $($packages.Count)."
}

$package = $packages[0]
$version = $package.Name.Substring('RazorConsole.Cli.'.Length)
$version = $version.Substring(0, $version.Length - '.nupkg'.Length)
Write-Host "Package: $($package.FullName)"
Write-Host "Version: $version"

$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName })

    foreach ($framework in 'net8.0', 'net10.0') {
        $folder = "tools/$framework/any"
        $settings = Read-Entry $archive "$folder/DotnetToolSettings.xml"
        if ($null -eq $settings) {
            Add-Problem "$folder/DotnetToolSettings.xml is missing."
        }
        else {
            if ($settings -notmatch 'Name="razorconsole"') {
                Add-Problem "$folder/DotnetToolSettings.xml does not declare the command 'razorconsole'."
            }

            if ($settings -notmatch 'EntryPoint="RazorConsole\.Cli\.dll"') {
                Add-Problem "$folder/DotnetToolSettings.xml does not use RazorConsole.Cli.dll as the entry point."
            }
        }

        foreach ($assembly in 'RazorConsole.Cli.dll', 'RazorConsole.Core.dll', 'RazorConsole.Testing.dll') {
            if ($names -notcontains "$folder/$assembly") {
                Add-Problem "$folder/$assembly is missing."
            }
        }
    }

    $unwanted = @($names | Where-Object { $_ -match '(^|/)RazorConsole\.Tests' -or $_ -match '\.(cs|razor)$' })
    foreach ($name in $unwanted) {
        Add-Problem "$name must not be packed."
    }

    $nuspecName = $names | Where-Object { $_ -like '*.nuspec' } | Select-Object -First 1
    $nuspec = if ($nuspecName) { Read-Entry $archive $nuspecName } else { $null }
    if ($null -eq $nuspec) {
        Add-Problem 'The package has no nuspec.'
    }
    else {
        if ($nuspec -notmatch '<packageType name="DotnetTool"') {
            Add-Problem 'The nuspec is not a DotnetTool package.'
        }

        if ($nuspec -notmatch '<readme>') {
            Add-Problem 'The nuspec has no readme.'
        }

        if ($nuspec -match '<dependency id="RazorConsole\.(Core|Testing)"') {
            Add-Problem 'The nuspec depends on RazorConsole.Core or RazorConsole.Testing, which are bundled in the tool.'
        }
    }

    $readme = Read-Entry $archive 'README.md'
    if ($null -eq $readme) {
        Add-Problem 'README.md is missing.'
    }
    elseif (-not $readme.TrimStart([char]0xFEFF).StartsWith('# RazorConsole.Cli')) {
        Add-Problem "README.md is not the tool's own readme (it must start with '# RazorConsole.Cli')."
    }

    $runtimeConfig = Read-Entry $archive 'tools/net8.0/any/RazorConsole.Cli.runtimeconfig.json'
    if ($null -eq $runtimeConfig) {
        Add-Problem 'tools/net8.0/any/RazorConsole.Cli.runtimeconfig.json is missing.'
    }
    elseif ($runtimeConfig -notmatch '"rollForward"\s*:\s*"Major"') {
        Add-Problem 'tools/net8.0/any/RazorConsole.Cli.runtimeconfig.json does not roll forward to a newer major runtime; the tool would not start where only a newer .NET is installed. Set <RollForward>Major</RollForward>.'
    }
}
finally {
    $archive.Dispose()
}

if ($problems.Count -gt 0) {
    throw "The package '$($package.Name)' has $($problems.Count) problem(s):`n - $($problems -join "`n - ")"
}

Write-Host '  ok    package contents'

if (-not $Install) {
    return
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('rc-cli-verify-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$toolPath = Join-Path $work 'tool'
$home_ = Join-Path $work 'home'
New-Item -ItemType Directory -Path $work | Out-Null
$previousHome = $env:RAZORCONSOLE_CLI_HOME
$previousLocation = Get-Location

# Only the package directory is a source, so what is installed is what was packed. Running from the work directory keeps
# the repository's global.json out of the way.
$config = Join-Path $work 'nuget.config'
$source = (Resolve-Path $PackageDirectory).Path
Set-Content -Path $config -Encoding UTF8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="package-under-test" value="$source" />
  </packageSources>
</configuration>
"@

$installed = $false
try {
    Set-Location $work

    # Not $install: variable names are case-insensitive, and that is the -Install switch.
    $installed_ = Invoke-Native 'dotnet' @('tool', 'install', 'RazorConsole.Cli', '--tool-path', $toolPath, '--configfile', $config, '--version', $version)
    if ($installed_.ExitCode -ne 0) {
        throw "dotnet tool install failed (exit $($installed_.ExitCode)):`n$($installed_.Output)"
    }

    $installed = $true
    Write-Host '  ok    dotnet tool install'

    $tool = Join-Path $toolPath 'razorconsole'
    if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        $tool += '.exe'
    }

    $reported = Invoke-Native $tool @('--version')
    if ($reported.ExitCode -ne 0 -or -not $reported.Output.StartsWith($version)) {
        throw "razorconsole --version printed '$($reported.Output)' (exit $($reported.ExitCode)), expected it to start with '$version'."
    }

    Write-Host "  ok    razorconsole --version ($($reported.Output))"

    # A state directory that does not exist yet, as on a user's first run.
    $env:RAZORCONSOLE_CLI_HOME = $home_
    $doctor = Invoke-Native $tool @('doctor', '--json')
    $result = $null
    try {
        $result = $doctor.Output | ConvertFrom-Json
    }
    catch {
        # Reported below with the raw output.
    }

    if ($doctor.ExitCode -ne 0 -or $null -eq $result -or $result.ok -ne $true) {
        throw "razorconsole doctor --json failed (exit $($doctor.ExitCode)):`n$($doctor.Output)"
    }

    $failed = @($result.checks | Where-Object { $_.status -eq 'fail' })
    if ($failed.Count -gt 0) {
        throw "razorconsole doctor reported failed checks:`n$($doctor.Output)"
    }

    Write-Host "  ok    razorconsole doctor ($(@($result.checks).Count) checks)"
}
finally {
    $env:RAZORCONSOLE_CLI_HOME = $home_
    $tool = Join-Path $toolPath 'razorconsole'
    if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        $tool += '.exe'
    }

    if (Test-Path $tool) {
        Invoke-Native $tool @('host', 'stop') | Out-Null
    }

    $env:RAZORCONSOLE_CLI_HOME = $previousHome
    if ($installed) {
        Invoke-Native 'dotnet' @('tool', 'uninstall', 'RazorConsole.Cli', '--tool-path', $toolPath) | Out-Null
    }

    Set-Location $previousLocation
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
