using Newtonsoft.Json;

namespace SRXDModManager.Library; 

internal class GitHubAsset {
    [JsonProperty("url")]
    public string Url { get; set; }
    
    [JsonProperty("name")]
    public string Name { get; set; }
}