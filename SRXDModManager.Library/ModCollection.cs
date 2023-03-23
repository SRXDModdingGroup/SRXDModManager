using System.Collections;
using System.Collections.Generic;

namespace SRXDModManager.Library; 

public class ModCollection : IEnumerable<Mod> {
    public int Count => mods.Count;
    
    private SortedDictionary<string, Mod> mods;

    public ModCollection() => mods = new SortedDictionary<string, Mod>();

    public bool TryGetMod(string name, out Mod mod) => mods.TryGetValue(name, out mod);

    public IEnumerator<Mod> GetEnumerator() => mods.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}