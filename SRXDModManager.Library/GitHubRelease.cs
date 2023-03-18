using System;
using Newtonsoft.Json;

namespace SRXDModManager.Library; 

public class GitHubRelease {
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
    public Asset[] Assets { get; set; } = Array.Empty<Asset>();
}