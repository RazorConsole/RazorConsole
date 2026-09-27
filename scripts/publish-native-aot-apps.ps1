param(
    [string] $RuntimeIdentifier,
    [string] $Version,
    [string] $OutputDirectory = "artifacts/native-aot",
    [string] $ManifestPath
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$galleryRoot = Join-Path $repositoryRoot "gallery"
$properties = @(
    "RazorConsolePublishNativeAot",
    "PackageId",
    "Title",
    "Description",
    "PackageProjectUrl",
    "RepositoryUrl",
    "ToolCommandName",
    "AssemblyName"
) -join ","

$apps = @(
    Get-ChildItem $galleryRoot -Filter *.csproj -Recurse | Sort-Object FullName | ForEach-Object {
        $projectPath = $_.FullName
        $result = & dotnet msbuild $projectPath -nologo "-getProperty:$properties"
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to evaluate MSBuild metadata for $projectPath."
        }

        $metadata = ($result -join [Environment]::NewLine) | ConvertFrom-Json
        $values = $metadata.Properties
        if ($values.RazorConsolePublishNativeAot -ne "true") {
            return
        }

        foreach ($requiredProperty in @("PackageId", "Title", "Description", "AssemblyName")) {
            if (-not $values.$requiredProperty) {
                throw "$projectPath enables RazorConsolePublishNativeAot but does not define $requiredProperty."
            }
        }

        $commandName = $values.ToolCommandName
        if (-not $commandName) {
            $commandName = $values.PackageId.Replace(".", "-").ToLowerInvariant()
        }

        [pscustomobject]@{
            packageId = $values.PackageId
            title = $values.Title
            description = $values.Description
            projectUrl = if ($values.PackageProjectUrl) { $values.PackageProjectUrl } else { $values.RepositoryUrl }
            repositoryUrl = $values.RepositoryUrl
            commandName = $commandName
            assemblyName = $values.AssemblyName
            project = [System.IO.Path]::GetRelativePath($repositoryRoot, $projectPath).Replace("\", "/")
        }
    }
)

if ($apps.Count -eq 0) {
    throw "No projects enabled RazorConsolePublishNativeAot."
}

$duplicatePackageIds = $apps | Group-Object packageId | Where-Object Count -gt 1
$duplicateCommands = $apps | Group-Object commandName | Where-Object Count -gt 1
if ($duplicatePackageIds) {
    throw "Duplicate Native AOT PackageId: $($duplicatePackageIds.Name -join ', ')."
}
if ($duplicateCommands) {
    throw "Duplicate Native AOT command name: $($duplicateCommands.Name -join ', ')."
}

$manifest = [pscustomobject]@{ apps = $apps }
if ($ManifestPath) {
    $resolvedManifestPath = if ([System.IO.Path]::IsPathRooted($ManifestPath)) {
        $ManifestPath
    } else {
        Join-Path $repositoryRoot $ManifestPath
    }
    $manifestDirectory = Split-Path $resolvedManifestPath -Parent
    New-Item -ItemType Directory -Force $manifestDirectory | Out-Null
    $manifest | ConvertTo-Json -Depth 5 | Set-Content $resolvedManifestPath
}

if (-not $RuntimeIdentifier) {
    $manifest | ConvertTo-Json -Depth 5
    return
}
if (-not $Version) {
    throw "Version is required when RuntimeIdentifier is provided."
}

$platformArtifact = switch -Regex ($RuntimeIdentifier) {
    '^linux-' { $RuntimeIdentifier; break }
    '^win-(.+)$' { "windows-$($Matches[1])"; break }
    '^osx-(.+)$' { "macos-$($Matches[1])"; break }
    default { throw "Unsupported runtime identifier: $RuntimeIdentifier" }
}
$archiveExtension = if ($RuntimeIdentifier.StartsWith("win-")) { "zip" } else { "tar.gz" }
$resolvedOutputDirectory = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repositoryRoot $OutputDirectory
}

foreach ($app in $apps) {
    $appOutput = Join-Path $resolvedOutputDirectory $app.commandName
    $publishDirectory = Join-Path $appOutput "publish"
    $archiveName = "$($app.commandName)-$Version-$platformArtifact"
    $stageDirectory = Join-Path $appOutput $archiveName

    if (Test-Path $appOutput) {
        Remove-Item $appOutput -Recurse -Force
    }
    New-Item -ItemType Directory -Force $publishDirectory | Out-Null

    & dotnet publish (Join-Path $repositoryRoot $app.project) `
        --configuration Release `
        --framework net10.0 `
        --runtime $RuntimeIdentifier `
        -p:PublishAot=true `
        -p:StripSymbols=true `
        --output $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Native AOT publish failed for $($app.packageId) on $RuntimeIdentifier."
    }

    Copy-Item $publishDirectory $stageDirectory -Recurse
    $executableExtension = if ($RuntimeIdentifier.StartsWith("win-")) { ".exe" } else { "" }
    $sourceExecutable = Join-Path $stageDirectory "$($app.assemblyName)$executableExtension"
    $destinationExecutable = Join-Path $stageDirectory "$($app.commandName)$executableExtension"
    if (-not (Test-Path $sourceExecutable)) {
        throw "Published executable was not found: $sourceExecutable"
    }
    if ($sourceExecutable -ne $destinationExecutable) {
        Move-Item $sourceExecutable $destinationExecutable
    }
    if (-not $RuntimeIdentifier.StartsWith("win-")) {
        & chmod +x $destinationExecutable
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to mark $destinationExecutable as executable."
        }
    }

    $archivePath = Join-Path $resolvedOutputDirectory "$archiveName.$archiveExtension"
    if ($archiveExtension -eq "zip") {
        Compress-Archive -Path $stageDirectory -DestinationPath $archivePath
    } else {
        & tar -C $appOutput -czf $archivePath $archiveName
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create $archivePath."
        }
    }
}
