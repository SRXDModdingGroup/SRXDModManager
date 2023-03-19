using System;
using Newtonsoft.Json;

namespace SRXDModManager.Library; 

internal class GitHubRelease {
    [JsonProperty("id")]
    public int Id { get; set; }
    
    [JsonProperty("name")]
    public string Name { get; set; }
    
    [JsonProperty("tag_name")]
    public string TagName { get; set; }
    
    [JsonProperty("body")]
    public string Body { get; set; }
    
    [JsonProperty("published_at")]
    public string PublishedAt { get; set; }

    [JsonProperty("assets")]
    public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
}