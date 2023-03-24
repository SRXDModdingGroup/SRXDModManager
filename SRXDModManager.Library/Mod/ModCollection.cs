using System;
using System.Collections;
using System.Collections.Generic;

namespace SRXDModManager.Library; 

public class ModCollection : IEnumerable<Mod> {
    public int Count => mods.Count;

    private readonly SortedDictionary<string, Mod> mods;

    public ModCollection() => mods = new SortedDictionary<string, Mod>();

    public ModCollection(ModCollection other) => mods = new SortedDictionary<string, Mod>(other.mods);

    public ModCollection(IEnumerable<Mod> mods) {
        this.mods = new SortedDictionary<string, Mod>();

        foreach (var mod in mods)
            TryAddMod(mod);
    }

    public void Clear() => mods.Clear();

    public bool TryAddMod(Mod mod) {
        if (mods.TryGetValue(mod.Name, out var existing) && mod.Version > existing.Version)
            return false;

        mods[mod.Name] = mod;

        return true;
    }

    public bool ContainsMod(string name) => mods.ContainsKey(name);

    public bool ContainsMod(string name, Version minimumVersion) {
        if (!mods.TryGetValue(name, out var mod))
            return false;

        return mod.Version >= minimumVersion;
    }

    public bool TryGetMod(string name, out Mod mod) => mods.TryGetValue(name, out mod);
    
    public bool TryGetMod(string name, Version minimumVersion, out Mod mod) {
        if (mods.TryGetValue(name, out mod) && mod.Version >= minimumVersion)
            return true;
        
        mod = null;
            
        return false;
    }

    public IEnumerator<Mod> GetEnumerator() => mods.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}