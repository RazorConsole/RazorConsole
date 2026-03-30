// Copyright (c) RazorConsole. All rights reserved.

using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace RazorConsole.Gallery.Services;

public interface INuGetUpgradeChecker
{
    ValueTask<UpgradeCheckResult> CheckForUpgradeAsync(CancellationToken cancellationToken = default);
}

public sealed record UpgradeCheckResult(bool HasUpgrade, string CurrentVersion, string? LatestVersion, Uri? PackageUri)
{
    public static UpgradeCheckResult WithUpdate(string currentVersion, string latestVersion, Uri? packageUri)
        => new(true, currentVersion, latestVersion, packageUri);

    public static UpgradeCheckResult WithoutUpdate(string currentVersion, string? latestVersion, Uri? packageUri)
        => new(false, currentVersion, latestVersion, packageUri);
}

internal sealed class NuGetUpgradeChecker : INuGetUpgradeChecker
{
    private const string PackageId = "RazorConsole.Gallery";
    private static readonly Uri PackagePage = new("https://www.nuget.org/packages/RazorConsole.Gallery/", UriKind.Absolute);

    private readonly HttpClient _httpClient;
    private readonly ILogger<NuGetUpgradeChecker> _logger;
    private readonly string _currentVersion;

    private UpgradeCheckResult? _cachedResult;
    private readonly SemaphoreSlim _cacheSemaphore = new(1, 1);

    public NuGetUpgradeChecker(HttpClient httpClient, ILogger<NuGetUpgradeChecker> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currentVersion = ResolveCurrentVersion();
    }

    public async ValueTask<UpgradeCheckResult> CheckForUpgradeAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedResult is not null)
        {
            return _cachedResult;
        }

        await _cacheSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedResult is not null)
            {
                return _cachedResult;
            }

            var result = await CheckForUpgradeCoreAsync(cancellationToken).ConfigureAwait(false);
            _cachedResult = result;
            return result;
        }
        finally
        {
            _cacheSemaphore.Release();
        }
    }

    private async Task<UpgradeCheckResult> CheckForUpgradeCoreAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_currentVersion))
        {
            return UpgradeCheckResult.WithoutUpdate(string.Empty, null, null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{PackageId.ToLowerInvariant()}/index.json");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("NuGet responded with {StatusCode} when checking for updates.", response.StatusCode);
                return UpgradeCheckResult.WithoutUpdate(_currentVersion, null, PackagePage);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("versions", out var versionsElement))
            {
                return UpgradeCheckResult.WithoutUpdate(_currentVersion, null, PackagePage);
            }

            NuGetVersion? latest = null;
            foreach (var element in versionsElement.EnumerateArray())
            {
                var versionText = element.GetString();
                if (string.IsNullOrWhiteSpace(versionText))
                {
                    continue;
                }

                if (!NuGetVersion.TryParse(versionText, out var candidate))
                {
                    continue;
                }

                if (latest is null || candidate > latest)
                {
                    latest = candidate;
                }
            }

            if (latest is null)
            {
                return UpgradeCheckResult.WithoutUpdate(_currentVersion, null, PackagePage);
            }

            var latestVersion = latest!;
            if (!NuGetVersion.TryParse(_currentVersion, out var parsed) || parsed is null)
            {
                parsed = latestVersion;
            }

            var currentVersion = parsed;

            // Normalize latest version by removing "-alpha" prefix if present
            var normalizedLatestVersionString = latestVersion.ToNormalizedString().Replace("-alpha.", "-");
            if (!NuGetVersion.TryParse(normalizedLatestVersionString, out var normalizedLatestVersion))
            {
                normalizedLatestVersion = latestVersion;
            }

            if (currentVersion >= normalizedLatestVersion)
            {
                return UpgradeCheckResult.WithoutUpdate(currentVersion.ToNormalizedString(), latestVersion.ToNormalizedString(), PackagePage);
            }

            return UpgradeCheckResult.WithUpdate(currentVersion.ToNormalizedString(), latestVersion.ToNormalizedString(), PackagePage);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Failed to query NuGet for gallery updates.");
            return UpgradeCheckResult.WithoutUpdate(_currentVersion, null, PackagePage);
        }
    }

    private static string ResolveCurrentVersion()
    {
        var assembly = typeof(NuGetUpgradeChecker).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            return informational;
        }

        var version = assembly.GetName().Version;
        return version?.ToString() ?? string.Empty;
    }
}
