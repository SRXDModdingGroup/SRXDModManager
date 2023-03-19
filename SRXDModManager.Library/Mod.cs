namespace SRXDModManager.Library; 

public class Mod {
    public string Directory { get; }
    
    public string Name { get; }
    
    public string Description { get; }
    
    public string Version { get; }
    
    public string Repository { get; }

    public ModDependency[] Dependencies { get; }

    internal Mod(string directory, ModManifest manifest) {
        Directory = directory;
        Name = manifest.Name;
        Description = manifest.Description;
        Version = manifest.Version;
        Repository = manifest.Repository;
        Dependencies = manifest.Dependencies;
    }

    public override string ToString() => $"{Name} {Version}";
}