using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SRXDModManager.Library;

public class GitHubClient {
    private HttpClient releasesClient;
    private HttpClient downloadsClient;

    public GitHubClient() {
        releasesClient = new HttpClient();
        releasesClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        releasesClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

        downloadsClient = new HttpClient();
        downloadsClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        downloadsClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
    }

    public async Task<GitHubRelease> GetLatestRelease(string repository) {
        var response = await releasesClient.GetAsync($"https://api.github.com/repos/{repository}/releases/latest");

        response.EnsureSuccessStatusCode();

        var release = JsonConvert.DeserializeObject<GitHubRelease>(await response.Content.ReadAsStringAsync());

        if (release == null)
            throw new JsonException("Could not deserialize GitHub release");

        return release;
    }

    public async Task<Stream> DownloadFile(string url) {
        var response = await downloadsClient.GetAsync(url);
        
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync();
    }
}