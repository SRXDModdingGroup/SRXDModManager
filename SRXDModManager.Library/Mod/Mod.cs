using System;

namespace SRXDModManager.Library; 

public class Mod {
    public string Directory { get; }
    
    public string Name { get; }
    
    public string Description { get; }
    
    public Version Version { get; }
    
    public string Repository { get; }

    public ModDependency[] Dependencies { get; }

    internal Mod(string directory, string name, string description, Version version, string repository, ModDependency[] dependencies) {
        Directory = directory;
        Name = name;
        Description = description;
        Version = version;
        Repository = repository;
        Dependencies = dependencies;
    }

    public override string ToString() => $"{Name} {Version}";
}