using Newtonsoft.Json;

namespace SRXDModManager.Library; 

public class Asset {
    [JsonProperty("url")]
    public string Url { get; set; }
    
    [JsonProperty("name")]
    public string Name { get; set; }
}