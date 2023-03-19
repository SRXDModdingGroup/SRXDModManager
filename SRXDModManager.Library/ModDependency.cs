using Newtonsoft.Json;

namespace SRXDModManager.Library; 

internal class ModDependency {
    [JsonProperty("name")]
    public string Name { get; set; }
    
    [JsonProperty("version")]
    public string Version { get; set; }
    
    [JsonProperty("repository")]
    public string Repository { get; set; }
}