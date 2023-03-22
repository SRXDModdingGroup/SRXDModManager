using System;

namespace SRXDModManager.Library; 

public class Mod {
    public string Name { get; }
    
    public string Description { get; }
    
    public Version Version { get; }
    
    public Repository Repository { get; }

    public ModDependency[] Dependencies { get; }

    internal Mod(string name, string description, Version version, Repository repository, ModDependency[] dependencies) {
        Name = name;
        Description = description;
        Version = version;
        Repository = repository;
        Dependencies = dependencies;
    }

    public override string ToString() => $"{Name} {Version}";
}