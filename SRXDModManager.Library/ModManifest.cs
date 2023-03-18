using System;
using Newtonsoft.Json;

namespace SRXDModManager.Library; 

public class ModManifest {
    [JsonProperty("name")]
    public string Name { get; set; }
    
    [JsonProperty("description")]
    public string Description { get; set; }
    
    [JsonProperty("version")]
    public string Version { get; set; }
    
    [JsonProperty("repository")]
    public string Repository { get; set; }

    [JsonProperty("dependencies")]
    public string[] Dependencies { get; set; } = Array.Empty<string>();
}