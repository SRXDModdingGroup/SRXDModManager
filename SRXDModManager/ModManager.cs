using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SRXDModManager.Library;

namespace SRXDModManager; 

public class ModManager {
    private const int MAX_GET_DEPENDENCIES_ITERATIONS = 8;
    
    private string gameDirectory;
    private string defaultOwner;
    private string pluginsDirectory;
    private ModsClient client;
    private ModCollection loadedMods;
    
    public ModManager(string gameDirectory, string defaultOwner) {
        this.gameDirectory = gameDirectory.Trim();
        this.defaultOwner = defaultOwner;
        pluginsDirectory = Path.Combine(this.gameDirectory, "BepInEx", "plugins");
        loadedMods = new ModCollection();
        client = new ModsClient();
    }
    
    public void RefreshMods() {
        lock (loadedMods) {
            loadedMods.Clear();

            if (!ModsUtility.GetAllModsInDirectory(pluginsDirectory)
                    .TryGetValue(out var mods, out string failureMessage)) {
                Console.WriteLine($"Failed to refresh mods: {failureMessage}");

                return;
            }

            foreach (var mod in mods)
                loadedMods.TryAddMod(mod);

            if (loadedMods.Count == 0)
                Console.WriteLine("No mods found");
            else {
                Console.WriteLine($"Found {loadedMods.Count} mod{(loadedMods.Count > 1 ? "s" : string.Empty)}:");
            
                foreach (var mod in loadedMods)
                    Console.WriteLine(mod);
            }
        }
    }

    public void SwitchBuild(ActiveBuild build) {
        if (!ModsUtility.GetActiveBuild(gameDirectory)
                .TryGetValue(out var activeBuild, out string failureMessage))
            Console.WriteLine($"Failed to get active build: {failureMessage}");
        else if (activeBuild == build)
            Console.WriteLine($"{build} is already the active build");
        else if (!ModsUtility.TrySetActiveBuild(build, gameDirectory)
                 .TryGetValue(out bool buildChanged, out failureMessage))
            Console.WriteLine($"Failed to switch build to {build}: {failureMessage}");
        else if (!buildChanged)
            Console.WriteLine($"{build} is already the active build");
        else
            Console.WriteLine($"Successfully switched builds to {build}");
    }
    
    public void GetModInfo(string modName) {
        lock (loadedMods) {
            if (!loadedMods.TryGetMod(modName, out var mod))
                Console.WriteLine($"{modName} not found");
            else
                Console.WriteLine($"{mod}: {mod.Description}");
        }
    }
    
    public void GetAllModInfo() {
        lock (loadedMods) {
            if (loadedMods.Count == 0)
                Console.WriteLine("No mods found");
            else {
                foreach (var mod in loadedMods)
                    Console.WriteLine($"{mod}: {mod.Description}");
            }
        }
    }

    public async Task Download(string address, bool resolveDependencies) {
        if (!Address.TryParse(address, out var addressObj)
            && !Address.TryParse($"{defaultOwner}/{address}", out addressObj))
            Console.WriteLine($"{address} is not a valid repository");
        else if (!resolveDependencies)
            await PerformDownload(addressObj);
        else if (!(await client.GetLatestModInfo(addressObj))
                 .TryGetValue(out var mod, out string failureMessage))
            Console.WriteLine($"Failed to get latest version of mod at {addressObj}: {failureMessage}");
        else {
            ModCollection loadedModsCopy;
        
            lock (loadedMods)
                loadedModsCopy = new ModCollection(loadedMods);
        
            await DownloadWithDependencies(new[] { mod }, loadedModsCopy);
        }
    }

    public async Task CheckForUpdate(string modName) {
        Mod mod;
        
        lock (loadedMods) {
            if (!loadedMods.TryGetMod(modName, out mod)) {
                Console.WriteLine($"{modName} not found");

                return;
            }
        }
        
        if (!(await client.GetLatestModInfo(mod.Address))
                 .TryGetValue(out var latestMod, out string failureMessage))
            Console.WriteLine($"Failed to get latest version of mod {mod.Name}: {failureMessage}");
        else if (latestMod.Version > mod.Version)
            Console.WriteLine($"{mod} is not up to date. Latest version is {latestMod.Version}");
        else {
            DependencyCollection missingDependencies;

            lock (loadedMods)
                missingDependencies = ModsUtility.GetMissingDependencies(mod, loadedMods);

            if (missingDependencies.Count > 0) {
                Console.WriteLine($"{mod} is missing dependencies:");

                foreach (var dependency in missingDependencies)
                    Console.WriteLine(dependency);
            }
            else
                Console.WriteLine($"{mod} is up to date");
        }
    }

    public async Task CheckAllForUpdates() {
        List<Mod> mods;
        
        lock (loadedMods)
            mods = new List<Mod>(loadedMods);

        var tasks = new Task<bool>[mods.Count];

        for (int i = 0; i < mods.Count; i++)
            tasks[i] = PerformCheckForUpdate(mods[i]);

        bool[] results = await Task.WhenAll(tasks);
        bool all = true;

        foreach (bool result in results)
            all &= result;

        if (all)
            Console.WriteLine("All mods are up to date");
    }

    public async Task Update(string modName, bool resolveDependencies) {
        ModCollection loadedModsCopy;
        
        lock (loadedMods)
            loadedModsCopy = new ModCollection(loadedMods);

        if (!loadedModsCopy.TryGetMod(modName, out var mod)) {
            Console.WriteLine($"{modName} not found");

            return;
        }
        
        if (!(await client.GetLatestModInfo(mod.Address))
            .TryGetValue(out var latestMod, out string failureMessage))
            Console.WriteLine($"Failed to get latest version of {mod.Name}: {failureMessage}");
        else if (resolveDependencies) {
            if (!await DownloadWithDependencies(new[] { latestMod }, loadedModsCopy))
                Console.WriteLine($"{mod} is up to date");
        }
        else if (mod.Version >= latestMod.Version)
            Console.WriteLine($"{mod} is up to date");
        else
            await PerformDownload(mod.Address);
    }

    public async Task UpdateAll(bool resolveDependencies) {
        ModCollection loadedModsCopy;

        lock (loadedMods)
            loadedModsCopy = new ModCollection(loadedMods);

        var loadedModsList = new List<Mod>(loadedModsCopy);
        var latestMods = new ModCollection(loadedModsCopy);
        var getLatestTasks = new Task<(Mod, Result<Mod>)>[loadedModsCopy.Count];

        for (int i = 0; i < loadedModsCopy.Count; i++) {
            getLatestTasks[i] = GetInfoForMod(loadedModsList[i]);
            
            async Task<(Mod, Result<Mod>)> GetInfoForMod(Mod mod)
                => (mod, await client.GetLatestModInfo(mod.Address));
        }

        var results = await Task.WhenAll(getLatestTasks);

        foreach (var (mod, result) in results) {
            if (result.TryGetValue(out var latestMod, out string failureMessage))
                latestMods.TryAddMod(latestMod);
            else
                Console.WriteLine($"Failed to get latest version of {mod.Name}: {failureMessage}");
        }

        if (resolveDependencies) {
            if (!await DownloadWithDependencies(latestMods, loadedModsCopy))
                Console.WriteLine("All mods are up to date");

            return;
        }

        bool all = true;
            
        foreach (var mod in latestMods) {
            if (loadedModsCopy.ContainsMod(mod.Name, mod.Version))
                continue;
                
            all = false;
            await PerformDownload(mod.Address);
        }
            
        if (all)
            Console.WriteLine("All mods are up to date");
    }

    private async Task PerformDownload(Address address) {
        if (!(await client.DownloadMod(address, pluginsDirectory))
            .TryGetValue(out var mod, out string failureMessage)) {
            Console.WriteLine($"Failed to download mod at {address}: {failureMessage}");
            
            return;
        }

        lock (loadedMods)
            loadedMods.TryAddMod(mod);

        Console.WriteLine($"Successfully downloaded {mod}");
    }

    private async Task<bool> PerformCheckForUpdate(Mod mod) {
        if (!(await client.GetLatestModInfo(mod.Address))
            .TryGetValue(out var latestMod, out string failureMessage))
            Console.WriteLine($"Failed to get latest version of {mod}: {failureMessage}");
        else if (latestMod.Version > mod.Version)
            Console.WriteLine($"{mod} is not up to date. Latest version is {latestMod.Version}");
        else {
            DependencyCollection missingDependencies;

            lock (loadedMods)
                missingDependencies = ModsUtility.GetMissingDependencies(mod, loadedMods);
            
            if (missingDependencies.Count > 0)
                Console.WriteLine($"{mod} is missing dependencies");
            else {
                Console.WriteLine($"{mod} is up to date");

                return true;
            }
        }

        return false;
    }

    private async Task<bool> DownloadWithDependencies(IEnumerable<Mod> mods, ModCollection modCollection) {
        var dependentMods = await GetDependenciesRecursively(mods);
        bool any = false;

        foreach (var mod in dependentMods) {
            if (modCollection.ContainsMod(mod.Name, mod.Version))
                continue;
            
            any = true;
            await PerformDownload(mod.Address);
        }

        return any;
    }

    private async Task<ModCollection> GetDependenciesRecursively(IEnumerable<Mod> mods) {
        ModCollection loadedModsCopy;

        lock (loadedMods)
            loadedModsCopy = new ModCollection(loadedMods);

        var modsToCheck = new ModCollection(mods);
        var checkedMods = new ModCollection();
        var dependenciesToCheck = new DependencyCollection();
        var getLatestTasks = new Dictionary<string, Task<(ModDependency, Result<Mod>)>>();

        for (int i = 0; i < MAX_GET_DEPENDENCIES_ITERATIONS && modsToCheck.Count > 0; i++) {
            foreach (var mod in modsToCheck) {
                checkedMods.TryAddMod(mod);
                
                foreach (var dependency in mod.Dependencies)
                    dependenciesToCheck.TryAddDependency(dependency);
            }
            
            modsToCheck.Clear();

            foreach (var dependency in dependenciesToCheck) {
                if (checkedMods.ContainsMod(dependency.Name, dependency.Version))
                    continue;
                
                if (loadedModsCopy.TryGetMod(dependency.Name, dependency.Version, out var dependentMod))
                    modsToCheck.TryAddMod(dependentMod);
                else if (!getLatestTasks.ContainsKey(dependency.Address.ToString()))
                    getLatestTasks.Add(dependency.Address.ToString(), GetInfoForDependency(dependency));
                
                async Task<(ModDependency, Result<Mod>)> GetInfoForDependency(ModDependency dependency)
                    => (dependency, await client.GetLatestModInfo(dependency.Address));
            }
            
            dependenciesToCheck.Clear();

            var results = await Task.WhenAll(getLatestTasks.Values);

            foreach (var (dependency, result) in results) {
                if (result.TryGetValue(out var mod, out string failureMessage))
                    modsToCheck.TryAddMod(mod);
                else
                    Console.WriteLine($"Failed to get latest version of {dependency.Name}: {failureMessage}");
            }
            
            getLatestTasks.Clear();
        }

        return checkedMods;
    }
}