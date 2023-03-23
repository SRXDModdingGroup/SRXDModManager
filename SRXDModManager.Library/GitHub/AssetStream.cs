using System.IO;

namespace SRXDModManager.Library; 

public readonly struct AssetStream {
    public string Name { get; }
    
    public Stream Stream { get; }

    public AssetStream(string name, Stream stream) {
        Name = name;
        Stream = stream;
    }
}