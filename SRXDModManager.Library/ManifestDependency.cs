using Newtonsoft.Json;

namespace SRXDModManager.Library; 

public class ManifestDependency {
    [JsonProperty("name")]
    public string Name { get; set; }
    
    [JsonProperty("version")]
    public string Version { get; set; }
    
    [JsonProperty("repository")]
    public string Repository { get; set; }
    
    internal ManifestDependency() { }
}