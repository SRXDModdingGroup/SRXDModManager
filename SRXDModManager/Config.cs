using Newtonsoft.Json;

namespace SRXDModManager; 

public class Config {
    [JsonProperty("gameDirectory")]
    public string GameDirectory { get; set; } = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Spin Rhythm";
}