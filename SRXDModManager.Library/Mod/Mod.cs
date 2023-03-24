using System;

namespace SRXDModManager.Library; 

public class Mod {
    public string Name { get; }
    
    public string Description { get; }
    
    public Version Version { get; }
    
    public Address Address { get; }

    public ModDependency[] Dependencies { get; }

    internal Mod(string name, string description, Version version, Address address, ModDependency[] dependencies) {
        Name = name;
        Description = description;
        Version = version;
        Address = address;
        Dependencies = dependencies;
    }

    public override string ToString() => $"{Name} {Version}";
}