using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MoneyShot.Services;

public sealed class AutoUpdateService
{
    private const string Owner = "Daolyap";
    private const string Repository = "MoneyShot";
    private const string LatestReleaseUrl = $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";
    private const string AllReleasesUrl = $"https://api.github.com/repos/{Owner}/{Repository}/releases?per_page=1";
    private static readonly Regex VersionNumberRegex = new(@"\d+", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;

    public AutoUpdateService(HttpClient? httpClient = null)
    {
        if (httpClient == null)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
        }
        else
        {
            _httpClient = httpClient;
        }

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MoneyShot", GetLocalVersion().ToString()));
        }

        if (!_httpClient.DefaultRequestHeaders.Accept.Any())
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        var token = Environment.GetEnvironmentVariable(""); // I can put a GitHub token in here if I want authenticated API requests
        if (!string.IsNullOrWhiteSpace(token) && _httpClient.DefaultRequestHeaders.Authorization == null)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }
    }

    public Version GetLocalVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version
            ?? Assembly.GetExecutingAssembly().GetName().Version
            ?? new Version(0, 0, 0, 0);
    }

    public async Task<UpdateInfo?> GetAvailableUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(stream, cancellationToken: cancellationToken);
            if (release == null)
            {
                return null;
            }

            var remoteVersion = ParseVersion(release.TagName);
            if (remoteVersion == null)
            {
                throw new InvalidOperationException(
                    $"Could not parse version from release tag '{release.TagName}'. Expected format: v1.2.3 or v1.2.3-build.N");
            }

            var localVersion = GetLocalVersion();
            if (CompareSemVer(remoteVersion, localVersion) <= 0)
            {
                return null;
            }

            var preferredAsset = SelectPreferredAsset(release.Assets);
            if (preferredAsset == null)
            {
                throw new InvalidOperationException(
                    $"Release {release.TagName} has no supported asset (.exe or .zip). " +
                    "MSI-only releases cannot be applied automatically.");
            }

            // Try to find a SHA256SUMS.txt asset and look up our asset's expected hash. If the
            // release is missing the sums file, we surface a non-fatal warning later in
            // StageAndPrepareUpdateAsync rather than refusing to update — old releases predate
            // this scheme and we don't want to brick existing installs.
            var expectedSha256 = await TryFindExpectedSha256Async(release.Assets, preferredAsset.Name, cancellationToken);

            return new UpdateInfo(localVersion, remoteVersion, preferredAsset.Name, preferredAsset.BrowserDownloadUrl, expectedSha256);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out while checking for updates.", ex);
        }
        catch (HttpRequestException ex)
        {
            var message = ex.StatusCode switch
            {
                HttpStatusCode.Forbidden => "GitHub Releases endpoint rejected the request (403). This is often due to API rate limits or network policy restrictions.",
                HttpStatusCode.Unauthorized => "GitHub Releases endpoint rejected the request (401). Public releases normally do not require authentication; if you configured MONEYSHOT_GITHUB_TOKEN, verify it is valid.",
                HttpStatusCode.NotFound => await HasOnlyPreReleasesAsync(cancellationToken)
                    ? "No stable releases are available. Daolyap should resolve this issue soon (unless he's dead)."
                    : "GitHub Releases endpoint returned 404. Please verify the repository release configuration.",
                _ => "Failed to contact GitHub Releases endpoint."
            };
            throw new InvalidOperationException(message, ex);
        }
    }

    private async Task<bool> HasOnlyPreReleasesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(AllReleasesUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var releases = await JsonSerializer.DeserializeAsync<List<GitHubReleaseResponse>>(stream, cancellationToken: cancellationToken);
            return releases?.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task StageAndPrepareUpdateAsync(UpdateInfo updateInfo, CancellationToken cancellationToken = default)
    {
        var currentExecutablePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExecutablePath))
        {
            throw new InvalidOperationException("Unable to determine the current executable path.");
        }

        var updateTempDirectory = Path.Combine(Path.GetTempPath(), "MoneyShot-Update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateTempDirectory);

        var downloadedAssetPath = Path.Combine(updateTempDirectory, updateInfo.AssetName);
        var stagedExecutablePath = Path.Combine(updateTempDirectory, Path.GetFileName(currentExecutablePath));

        try
        {
            await DownloadFileAsync(updateInfo.DownloadUrl, downloadedAssetPath, cancellationToken);

            // Refuse to install a binary whose hash doesn't match the publisher's SHA256SUMS.txt
            // — a mismatch could indicate a tampered release, an interrupted download, or a
            // mirror serving an old asset. Releases without a sums file are allowed through with
            // a logged warning so we don't block updates from older releases.
            if (!string.IsNullOrEmpty(updateInfo.ExpectedSha256))
            {
                var actualSha256 = await ComputeSha256Async(downloadedAssetPath, cancellationToken);
                if (!string.Equals(actualSha256, updateInfo.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Update integrity check failed. Expected SHA-256 {updateInfo.ExpectedSha256} but downloaded asset has SHA-256 {actualSha256}. Refusing to install.");
                }
            }
            else
            {
                MoneyShot.Services.Logger.Warn($"Release for {updateInfo.AssetName} has no SHA256SUMS.txt — installing without integrity verification.");
            }

            var executableToInstall = PrepareExecutableAsset(downloadedAssetPath, stagedExecutablePath);

            var scriptPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(updateTempDirectory, "apply-update.bat")
                : Path.Combine(updateTempDirectory, "apply-update.sh");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.WriteAllText(scriptPath, BuildWindowsSwapScript(currentExecutablePath, executableToInstall));
            }
            else
            {
                File.WriteAllText(scriptPath, BuildUnixSwapScript(currentExecutablePath, executableToInstall));
            }

            StartUpdateScript(scriptPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Permission denied while preparing the update files.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("File access failed while preparing the update.", ex);
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await downloadStream.CopyToAsync(fileStream, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out while downloading the update asset.", ex);
        }
    }

    private static string PrepareExecutableAsset(string downloadedAssetPath, string stagedExecutablePath)
    {
        if (downloadedAssetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            File.Move(downloadedAssetPath, stagedExecutablePath, true);
            return stagedExecutablePath;
        }

        if (downloadedAssetPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(downloadedAssetPath);
            var executableEntry = archive.Entries.FirstOrDefault(entry =>
                entry.Name.Equals(Path.GetFileName(stagedExecutablePath), StringComparison.OrdinalIgnoreCase));

            if (executableEntry == null)
            {
                throw new InvalidOperationException($"Update ZIP does not contain expected executable: {Path.GetFileName(stagedExecutablePath)}.");
            }

            executableEntry.ExtractToFile(stagedExecutablePath, true);
            return stagedExecutablePath;
        }

        throw new InvalidOperationException("Unsupported update asset type. Expected .exe or .zip.");
    }

    private async Task<string?> TryFindExpectedSha256Async(IReadOnlyList<ReleaseAsset>? assets, string assetName, CancellationToken cancellationToken)
    {
        if (assets == null) return null;
        var sumsAsset = assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        if (sumsAsset == null) return null;

        try
        {
            using var response = await _httpClient.GetAsync(sumsAsset.BrowserDownloadUrl, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseSha256Sums(text, assetName);
        }
        catch
        {
            // Network or parse failure — fall through to "no expected hash" rather than refuse.
            return null;
        }
    }

    /// <summary>
    /// Parses standard `sha256sum`-style output: each line is "&lt;hex hash&gt;  &lt;file name&gt;".
    /// Returns the hash for the requested filename or null if not present.
    /// </summary>
    internal static string? ParseSha256Sums(string sumsContent, string assetName)
    {
        if (string.IsNullOrWhiteSpace(sumsContent)) return null;
        foreach (var rawLine in sumsContent.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            // Split on whitespace, allowing for either single-space or two-space separators
            // and an optional binary-mode `*` prefix in front of the filename.
            var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            var hash = parts[0].Trim();
            var name = parts[1].TrimStart('*').Trim();
            if (name.Equals(assetName, StringComparison.OrdinalIgnoreCase) && hash.Length == 64)
            {
                return hash;
            }
        }
        return null;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static ReleaseAsset? SelectPreferredAsset(IReadOnlyList<ReleaseAsset>? assets)
    {
        if (assets == null || assets.Count == 0)
        {
            return null;
        }

        return assets.FirstOrDefault(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            ?? assets.FirstOrDefault(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    internal static int CompareSemVer(Version a, Version b)
    {
        var aMajor = a.Major;
        var bMajor = b.Major;
        if (aMajor != bMajor) return aMajor.CompareTo(bMajor);

        var aMinor = a.Minor;
        var bMinor = b.Minor;
        if (aMinor != bMinor) return aMinor.CompareTo(bMinor);

        var aPatch = a.Build < 0 ? 0 : a.Build;
        var bPatch = b.Build < 0 ? 0 : b.Build;
        return aPatch.CompareTo(bPatch);
    }

    internal static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var matches = VersionNumberRegex.Matches(value);
        if (matches.Count < 3)
        {
            return null;
        }

        static int SafeParse(Match match)
        {
            return int.TryParse(match.Value, out var parsed) && parsed >= 0 ? parsed : 0;
        }

        var major = SafeParse(matches[0]);
        var minor = SafeParse(matches[1]);
        var patch = SafeParse(matches[2]);
        var build = matches.Count > 3 ? SafeParse(matches[3]) : 0;
        return new Version(major, minor, patch, build);
    }

    private static void StartUpdateScript(string scriptPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Path.GetTempPath()
            });
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start the Windows update script.");
            }
            return;
        }

        var shellProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "sh",
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Path.GetTempPath()
        });
        if (shellProcess == null)
        {
            throw new InvalidOperationException("Failed to start the Unix update script.");
        }
    }

    private static string BuildWindowsSwapScript(string targetExecutablePath, string stagedExecutablePath)
    {
        var targetExecutableName = Path.GetFileName(targetExecutablePath);
        var escapedTargetPath = EscapeBatchValue(targetExecutablePath);
        var escapedStagedPath = EscapeBatchValue(stagedExecutablePath);
        var escapedTargetExecutableName = EscapeBatchValue(targetExecutableName);

        return $"""
        @echo off
        setlocal
        
        set "TARGET={escapedTargetPath}"
        set "STAGED={escapedStagedPath}"
        
        :WAIT_LOOP
        tasklist /FI "IMAGENAME eq {escapedTargetExecutableName}" 2>NUL | find /I "{escapedTargetExecutableName}" >NUL
        if not errorlevel 1 (
            timeout /T 1 /NOBREAK >NUL
            goto WAIT_LOOP
        )
        
        move /Y "%STAGED%" "%TARGET%"
        if errorlevel 1 exit /B 1
        
        start "" "%TARGET%"
        (goto) 2>nul & del "%~f0"
        """;
    }

    private static string BuildUnixSwapScript(string targetExecutablePath, string stagedExecutablePath)
    {
        var escapedTargetPath = EscapeShellSingleQuoted(targetExecutablePath);
        var escapedStagedPath = EscapeShellSingleQuoted(stagedExecutablePath);

        return $"""
        #!/bin/sh
        TARGET={escapedTargetPath}
        STAGED={escapedStagedPath}
        
        while pgrep -f "$TARGET" >/dev/null 2>&1; do
          sleep 1
        done
        
        mv -f "$STAGED" "$TARGET"
        chmod +x "$TARGET"
        "$TARGET" >/dev/null 2>&1 &
        rm -- "$0"
        """;
    }

    private static string EscapeBatchValue(string value) => value.Replace("%", "%%", StringComparison.Ordinal);

    private static string EscapeShellSingleQuoted(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    internal sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public List<ReleaseAsset>? Assets { get; set; }
    }

    internal sealed class ReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}

public sealed record UpdateInfo(Version LocalVersion, Version RemoteVersion, string AssetName, string DownloadUrl, string? ExpectedSha256 = null);