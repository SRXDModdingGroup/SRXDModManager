using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SRXDModManager.Library;

internal class GitHubClient {
    private HttpClient releasesClient;
    private HttpClient downloadsClient;
    private JsonSerializer serializer;

    public GitHubClient() {
        releasesClient = new HttpClient();
        releasesClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        releasesClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

        downloadsClient = new HttpClient();
        downloadsClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        downloadsClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

        serializer = new JsonSerializer();
    }

    public async Task<Result<GitHubRelease>> GetLatestRelease(string repository) {
        string url = $"https://api.github.com/repos/{repository}/releases/latest";
        var response = await releasesClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
            return Result<GitHubRelease>.Failure($"GET request to url {url} failed");

        using var textReader = new JsonTextReader(new StreamReader(await response.Content.ReadAsStreamAsync()));
        var release = serializer.Deserialize<GitHubRelease>(textReader);

        if (release == null)
            return Result<GitHubRelease>.Failure("Could not deserialize GitHub release");

        return Result<GitHubRelease>.Success(release);
    }

    public async Task<Result<Stream>> DownloadAsset(GitHubAsset asset) {
        var response = await downloadsClient.GetAsync(asset.Url);
        
        if (!response.IsSuccessStatusCode)
            return Result<Stream>.Failure($"GET request to url {asset.Url} failed");

        return Result<Stream>.Success(await response.Content.ReadAsStreamAsync());
    }
}