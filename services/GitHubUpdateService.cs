using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaPlay.Services
{
    public sealed class GitHubUpdateInfo
    {
        public bool Success { get; set; }
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseName { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public string AssetDownloadUrl { get; set; } = string.Empty;
        public long AssetSizeBytes { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public static class GitHubUpdateService
    {
        public const string Owner = "guiasysstudio";
        public const string Repository = "AlphaPlay";
        private const string LatestReleaseUrl = "https://api.github.com/repos/guiasysstudio/AlphaPlay/releases/latest";

        public static string GetCurrentVersion()
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null)
            {
                return "1.0.0";
            }

            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        public static async Task<GitHubUpdateInfo> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            GitHubUpdateInfo result = new()
            {
                CurrentVersion = GetCurrentVersion()
            };

            try
            {
                using HttpClient client = CreateClient();
                using HttpResponseMessage response = await client.GetAsync(LatestReleaseUrl, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    result.Success = false;
                    result.Message = response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "Nenhuma Release foi encontrada no GitHub. Crie um lançamento com o instalador anexado."
                        : $"Não foi possível consultar o GitHub. Código: {(int)response.StatusCode}.";
                    return result;
                }

                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                JsonElement root = document.RootElement;

                string tag = GetString(root, "tag_name");
                string releaseName = GetString(root, "name");
                string releaseUrl = GetString(root, "html_url");
                string latestVersion = NormalizeVersion(tag);

                result.LatestVersion = latestVersion;
                result.ReleaseName = string.IsNullOrWhiteSpace(releaseName) ? tag : releaseName;
                result.ReleaseUrl = releaseUrl;

                if (string.IsNullOrWhiteSpace(latestVersion))
                {
                    result.Success = false;
                    result.Message = "A última Release não possui uma tag válida, exemplo: v1.0.0.";
                    return result;
                }

                if (!TryFindInstallerAsset(root, out string assetName, out string assetUrl, out long assetSize))
                {
                    result.Success = false;
                    result.Message = "A última Release foi encontrada, mas não possui um instalador .exe anexado.";
                    return result;
                }

                result.AssetName = assetName;
                result.AssetDownloadUrl = assetUrl;
                result.AssetSizeBytes = assetSize;
                result.HasUpdate = CompareVersions(latestVersion, result.CurrentVersion) > 0;
                result.Success = true;
                result.Message = result.HasUpdate
                    ? "Nova atualização disponível."
                    : "Seu AlphaPlay já está na versão mais recente.";

                return result;
            }
            catch (TaskCanceledException)
            {
                result.Success = false;
                result.Message = "A consulta ao GitHub demorou demais ou foi cancelada.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Erro ao procurar atualização: {ex.Message}";
                return result;
            }
        }

        public static async Task<string> DownloadInstallerAsync(
            GitHubUpdateInfo updateInfo,
            IProgress<(long downloadedBytes, long totalBytes)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(updateInfo.AssetDownloadUrl))
            {
                throw new InvalidOperationException("Link do instalador não disponível.");
            }

            string updatesFolder = Path.Combine(AppFolderService.GetAppDataFolder(), "Updates");
            Directory.CreateDirectory(updatesFolder);

            string safeFileName = string.IsNullOrWhiteSpace(updateInfo.AssetName)
                ? $"AlphaPlay_Setup_{updateInfo.LatestVersion}.exe"
                : updateInfo.AssetName;

            string destinationPath = Path.Combine(updatesFolder, safeFileName);

            using HttpClient client = CreateClient();
            using HttpResponseMessage response = await client.GetAsync(updateInfo.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? updateInfo.AssetSizeBytes;
            long downloadedBytes = 0;

            await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream fileStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;
                progress?.Report((downloadedBytes, totalBytes));
            }

            return destinationPath;
        }

        private static HttpClient CreateClient()
        {
            HttpClient client = new()
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AlphaPlay-Updater/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        private static bool TryFindInstallerAsset(JsonElement releaseRoot, out string assetName, out string assetUrl, out long assetSize)
        {
            assetName = string.Empty;
            assetUrl = string.Empty;
            assetSize = 0;

            if (!releaseRoot.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            JsonElement? selected = null;
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string name = GetString(asset, "name");
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains("AlphaPlay", StringComparison.OrdinalIgnoreCase))
                {
                    selected = asset;
                    break;
                }

                if (selected == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    selected = asset;
                }
            }

            if (selected == null)
            {
                return false;
            }

            JsonElement chosen = selected.Value;
            assetName = GetString(chosen, "name");
            assetUrl = GetString(chosen, "browser_download_url");
            assetSize = chosen.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long size)
                ? size
                : 0;

            return !string.IsNullOrWhiteSpace(assetUrl);
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
        }

        private static string NormalizeVersion(string version)
        {
            return (version ?? string.Empty).Trim().TrimStart('v', 'V');
        }

        private static int CompareVersions(string available, string current)
        {
            if (!Version.TryParse(NormalizeVersion(available), out Version? availableVersion))
            {
                return 0;
            }

            if (!Version.TryParse(NormalizeVersion(current), out Version? currentVersion))
            {
                return 1;
            }

            return availableVersion.CompareTo(currentVersion);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 MB";
            }

            double mb = bytes / 1024d / 1024d;
            return $"{mb:0.0} MB";
        }
    }
}
