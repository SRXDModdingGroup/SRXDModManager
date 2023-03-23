using System.Collections;
using System.Collections.Generic;

namespace SRXDModManager.Library; 

public class ModCollection : IEnumerable<Mod> {
    public int Count => mods.Count;

    private readonly SortedDictionary<string, Mod> mods;

    public ModCollection() => mods = new SortedDictionary<string, Mod>();

    public void SetMod(Mod mod) => mods[mod.Name] = mod;

    public void Clear() => mods.Clear();

    public bool ContainsMod(string name) => mods.ContainsKey(name);

    public bool TryGetMod(string name, out Mod mod) => mods.TryGetValue(name, out mod);

    public IEnumerator<Mod> GetEnumerator() => mods.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}