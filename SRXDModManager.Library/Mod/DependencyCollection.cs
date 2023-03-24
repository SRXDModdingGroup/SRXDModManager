using System;
using System.Collections;
using System.Collections.Generic;

namespace SRXDModManager.Library; 

public class DependencyCollection : IEnumerable<ModDependency> {
    public int Count => dependencies.Count;

    private readonly SortedDictionary<string, ModDependency> dependencies;

    public DependencyCollection() => dependencies = new SortedDictionary<string, ModDependency>();

    public DependencyCollection(DependencyCollection other) => dependencies = new SortedDictionary<string, ModDependency>(other.dependencies);

    public DependencyCollection(IEnumerable<ModDependency> dependencies) {
        this.dependencies = new SortedDictionary<string, ModDependency>();

        foreach (var mod in dependencies)
            TryAddDependency(mod);
    }

    public void Clear() => dependencies.Clear();

    public bool TryAddDependency(ModDependency dependency) {
        if (dependencies.TryGetValue(dependency.Name, out var existing) && dependency.Version > existing.Version)
            return false;

        dependencies[dependency.Name] = dependency;

        return true;
    }

    public bool ContainsDependency(string name) => dependencies.ContainsKey(name);

    public bool ContainsDependency(string name, Version minimumVersion) {
        if (!dependencies.TryGetValue(name, out var dependency))
            return false;

        return dependency.Version >= minimumVersion;
    }

    public bool TryGetDependency(string name, out ModDependency mod) => dependencies.TryGetValue(name, out mod);
    
    public bool TryGetDependency(string name, Version minimumVersion, out ModDependency dependency) {
        if (dependencies.TryGetValue(name, out dependency) && dependency.Version >= minimumVersion)
            return true;
        
        dependency = null;
            
        return false;
    }

    public IEnumerator<ModDependency> GetEnumerator() => dependencies.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}