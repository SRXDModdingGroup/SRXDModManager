using System;
using Newtonsoft.Json;

namespace SRXDModManager.Library; 

internal class GitHubRelease {
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("assets")]
    public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
}