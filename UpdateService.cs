using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StudioLog
{
    internal class UpdateService : IDisposable
    {
        private const string ApiUrl = "https://api.github.com/repos/casey-tmc97/StudioLog/releases/latest";
        private readonly HttpClient _http;

        public UpdateService()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", $"StudioLog/{versionStr}");
        }

        public record ReleaseInfo(string TagName, string InstallerUrl, Version LatestVersion);

        public async Task<ReleaseInfo?> GetLatestReleaseAsync()
        {
            var json = await _http.GetFromJsonAsync<GitHubRelease>(ApiUrl);
            if (json == null) return null;

            if (!Version.TryParse(json.TagName.TrimStart('v'), out var latestVersion))
                return null;

            string? installerUrl = null;
            foreach (var asset in json.Assets)
            {
                if (asset.Name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase))
                {
                    installerUrl = asset.BrowserDownloadUrl;
                    break;
                }
            }

            if (installerUrl == null) return null;
            return new ReleaseInfo(json.TagName, installerUrl, latestVersion);
        }

        public bool IsUpdateAvailable(ReleaseInfo release)
        {
            var current = Assembly.GetExecutingAssembly().GetName().Version;
            if (current == null) return false;
            var currentNormalized = new Version(current.Major, current.Minor, current.Build);
            return release.LatestVersion > currentNormalized;
        }

        public async Task<string> DownloadInstallerAsync(
            ReleaseInfo release,
            IProgress<(long downloaded, long total)>? progress = null)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "StudioLog-Setup.exe");
            using var response = await _http.GetAsync(
                release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var file = File.Create(tempPath);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                progress?.Report((downloaded, total));
            }

            return tempPath;
        }

        public void LaunchInstallerAndExit(string installerPath)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /NORESTART",
                UseShellExecute = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("installer-launch-failed");

            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }

        private class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("assets")]
            public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
        }

        private class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;
        }
    }
}
