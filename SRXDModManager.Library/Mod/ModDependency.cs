using System;

namespace SRXDModManager.Library; 

public class ModDependency {
    public string Name { get; }
    
    public Version Version { get; }
    
    public Repository Repository { get; }

    internal ModDependency(string name, Version version, Repository repository) {
        Name = name;
        Version = version;
        Repository = repository;
    }

    public override string ToString() => $"{Name} {Version}";
}