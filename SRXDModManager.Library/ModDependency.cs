using Newtonsoft.Json;

namespace SRXDModManager.Library; 

public class ModDependency {
    [JsonProperty("name")]
    public string Name { get; set; }
    
    [JsonProperty("version")]
    public string Version { get; set; }
    
    [JsonProperty("repository")]
    public string Repository { get; set; }
    
    internal ModDependency() { }
}