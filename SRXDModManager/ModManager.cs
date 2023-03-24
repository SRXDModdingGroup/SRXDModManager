using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SRXDModManager.Library;

namespace SRXDModManager; 

public class ModManager {
    private const int MAX_GET_DEPENDENCIES_ITERATIONS = 8;
    
    private string gameDirectory;
    private string defaultRepository;
    private string pluginsDirectory;
    private ModsClient client;
    private ModCollection loadedMods;
    
    public ModManager(string gameDirectory, string defaultRepository) {
        this.gameDirectory = gameDirectory.Trim();
        this.defaultRepository = defaultRepository;
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

    public async Task Download(string repositoryName, bool resolveDependencies) {
        if (!Address.TryParse(repositoryName, out var repository)) {
            string withDefault = $"{defaultRepository}/{repositoryName}";
            
            if (!Address.TryParse(withDefault, out repository)) {
                Console.WriteLine($"{repositoryName} is not a valid repository name");
                
                return;
            }
        }

        if (!resolveDependencies) {
            await PerformDownload(repository);
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
            IReadOnlyList<ModDependency> missingDependencies;

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

        bool all = true;

        foreach (var task in tasks)
            all &= await task;

        if (all)
            Console.WriteLine("All mods are up to date");
    }

    public async Task Update(string modName, bool resolveDependencies) {
        Mod mod;
        
        lock (loadedMods) {
            if (!loadedMods.TryGetMod(modName, out mod)) {
                Console.WriteLine($"{modName} not found");

                return;
            }
        }

        if (!resolveDependencies) {
            await Download(new[] { mod });

            return;
        }
        else {
            await Update(new[] { mod });
        }
        
        if (!(await client.GetLatestModInfo(mod.Address))
            .TryGetValue(out var latestMod, out string failureMessage)) {
            Console.WriteLine($"Failed to check {mod.Name} for update: {failureMessage}");

            return;
        }

        bool upToDate = mod.Version >= latestMod.Version;

        if (!resolveDependencies) {
            if (upToDate)
                Console.WriteLine($"{mod} is up to date");
            else
                await PerformDownload(mod.Address);

            return;
        }

        var modsToDownload = new ModCollection();
        var modsToCheck = new Queue<Mod>();

        ModCollection resultingMods;

        lock (loadedMods)
            resultingMods = new ModCollection(loadedMods);

        if (upToDate)
            modsToCheck.Enqueue(mod);
        else {
            modsToCheck.Enqueue(latestMod);
            modsToDownload.TryAddMod(latestMod);
        }
    }

    public async Task UpdateAll(bool resolveDependencies) {
        List<Mod> mods;
        
        lock (loadedMods)
            mods = new List<Mod>(loadedMods);

        var modsToUpdate = new List<Mod>();
        var tasks = new Task<bool>[mods.Count];
        
        for (int i = 0; i < mods.Count; i++)
            tasks[i] = PerformGetUpdate(mods[i]);

        for (int i = 0; i < tasks.Length; i++) {
            if (await tasks[i])
                continue;

            modsToUpdate.Add(mods[i]);
        }

        if (!resolveDependencies) {
            if (modsToUpdate.Count == 0)
                Console.WriteLine("All mods are up to date");
            else {
                foreach (var mod in modsToUpdate)
                    await PerformDownload(mod.Address);
            }
            
            return;
        }
        
        
    }
    
    private static Result VerifyDirectoryExists(string directory) {
        if (!Directory.Exists(directory))
            return Result.Failure($"Could not find directory {directory}");
        
        return Result.Success();
    }

    private static Result VerifyFileExists(string path) {
        if (!File.Exists(path))
            return Result.Failure($"Could not find file {path}");
        
        return Result.Success();
    }

    private async Task PerformDownload(Address address) {
        if (!(await client.DownloadMod(address, gameDirectory))
            .TryGetValue(out var mod, out string failureMessage)) {
            Console.WriteLine($"Failed to download mod at {address}: {failureMessage}");
            
            return;
        }

        lock (loadedMods)
            loadedMods.TryAddMod(mod);

        Console.WriteLine($"Successfully downloaded {mod}");
    }

    private async Task Update(IReadOnlyList<Mod> mods, bool force, bool resolveDependencies) {
        var tasks = new Task<Result<Mod>>[mods.Count];
        
        for (int i = 0; i < mods.Count; i++)
            tasks[i] = client.GetLatestModInfo(mods[i].Address);

        var latestMods = new Mod[mods.Count];

        for (int i = 0; i < tasks.Length; i++) {
            if ((await tasks[i]).TryGetValue(out var latestMod, out string failureMessage))
                latestMods[i] = latestMod;
            else {
                latestMods[i] = mods[i];
                Console.WriteLine($"Failed to get latest version of {mods[i].Name}: {failureMessage}");
            }
        }
        
        var modsToDownload = new ModCollection();
        var modsToCheck = new Queue<Mod>();

        ModCollection resultingMods;

        lock (loadedMods)
            resultingMods = new ModCollection(loadedMods);
        
        foreach (var mod in mods) {
            if (!(await client.GetLatestModInfo(mod.Address))
                .TryGetValue(out var latestMod, out string failureMessage)) {
                Console.WriteLine($"Failed to get latest version of {mod.Name}: {failureMessage}");

                return;
            }

            if (mod.Version < latestMod.Version) {
                modsToDownload.TryAddMod(latestMod);
                resultingMods.TryAddMod(latestMod);
                modsToCheck.Enqueue(latestMod);
            }
            else {
                Console.WriteLine($"{mod} is up to date");
                modsToCheck.Enqueue(mod);
            }
        }
        
        
    }
    
    private async Task<bool> PerformCheckForUpdate(Mod mod) {
        if (!(await client.GetLatestModInfo(mod.Address))
            .TryGetValue(out var latestMod, out string failureMessage))
            Console.WriteLine($"Failed to get latest version of {mod}: {failureMessage}");
        else if (latestMod.Version > mod.Version)
            Console.WriteLine($"{mod} is not up to date. Latest version is {latestMod.Version}");
        else {
            IReadOnlyList<ModDependency> missingDependencies;

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
    
    private async Task<bool> PerformGetUpdate(Mod mod) {
        if ((await client.GetLatestModInfo(mod.Address))
            .TryGetValue(out var latestMod, out string failureMessage))
            return mod.Version >= latestMod.Version;
        
        Console.WriteLine($"Failed to get latest version of {mod.Name}: {failureMessage}");

        return false;
    }

    private async Task<DependencyCollection> GetDependenciesRecursively(IEnumerable<Mod> mods) {
        ModCollection loadedModsCopy;

        lock (loadedMods)
            loadedModsCopy = new ModCollection(loadedMods);

        var modsToCheck = new ModCollection(mods);
        var checkedMods = new ModCollection();
        var dependenciesToCheck = new DependencyCollection();
        var getLatestTasks = new Dictionary<Address, (ModDependency, Task<Result<Mod>>)>();

        for (int i = 0; i < MAX_GET_DEPENDENCIES_ITERATIONS && modsToCheck.Count > 0; i++) {
            foreach (var mod in modsToCheck) {
                checkedMods.TryAddMod(mod);
                
                foreach (var dependency in mod.Dependencies)
                    dependenciesToCheck.TryAddDependency(dependency);
            }

            foreach (var dependency in dependenciesToCheck) {
                if (checkedMods.ContainsMod(dependency.Name, dependency.Version))
                    continue;
                
                if (loadedModsCopy.TryGetMod(dependency.Name, dependency.Version, out var dependentMod))
                    modsToCheck.TryAddMod(dependentMod);
                else if (!getLatestTasks.ContainsKey(dependency.Address))
                    getLatestTasks.Add(dependency.Address, (dependency, client.GetLatestModInfo(dependency.Address)));
            }
            
            dependenciesToCheck.Clear();

            foreach (var (dependency, task) in getLatestTasks.Values) {
                if ((await task).TryGetValue(out var mod, out string failureMessage))
                    modsToCheck.TryAddMod(mod);
                else
                    Console.WriteLine($"Failed to get latest version of {dependency.Name}: {failureMessage}");
            }
            
            getLatestTasks.Clear();
        }
        
        var dependencies = new DependencyCollection();

        foreach (var mod in checkedMods)
            dependencies.TryAddDependency(new ModDependency(mod));

        return dependencies;
    }
}