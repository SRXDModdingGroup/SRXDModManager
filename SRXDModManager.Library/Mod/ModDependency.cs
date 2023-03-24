using System;

namespace SRXDModManager.Library; 

public class ModDependency {
    public string Name { get; }
    
    public Version Version { get; }
    
    public Address Address { get; }

    public ModDependency(Mod mod) : this(mod.Name, mod.Version, mod.Address) { }

    internal ModDependency(string name, Version version, Address address) {
        Name = name;
        Version = version;
        Address = address;
    }

    public override string ToString() => $"{Name} {Version}";
}