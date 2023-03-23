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

            if (!VerifyDirectoryExists(gameDirectory)
                    .Then(() => VerifyDirectoryExists(pluginsDirectory))
                    .TryGetValue(out string failureMessage)) {
                Console.WriteLine($"Failed to refresh mods: {failureMessage}");

                return;
            }

            foreach (string directory in Directory.GetDirectories(pluginsDirectory)) {
                if (ModsUtility.TryGetModFromDirectory(directory, out var mod))
                    loadedMods.SetMod(mod);
            }
            
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
    
    public void GetModInfo(string name) {
        lock (loadedMods) {
            if (!loadedMods.TryGetMod(name, out var mod))
                Console.WriteLine($"{name} not found");
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

    public async Task Download(string repository, bool resolveDependencies) {
        if (!Repository.TryParse(repository, out var repositoryObj)) {
            string withDefault = $"{defaultRepository}/{repository}";
            
            if (!Repository.TryParse(withDefault, out repositoryObj)) {
                Console.WriteLine($"{repository} is not a valid repository name");
                
                return;
            }
        }

        if (!resolveDependencies) {
            await PerformDownload(repositoryObj);
            
            return;
        }
    }

    public async Task CheckForUpdate(string name) {
        Mod mod;
        
        lock (loadedMods) {
            if (!loadedMods.TryGetMod(name, out mod)) {
                Console.WriteLine($"{name} not found");

                return;
            }
        }
        
        if (!(await client.GetLatestModInfo(mod.Repository))
                 .TryGetValue(out var latestMod, out string failureMessage))
            Console.WriteLine($"Failed to check {mod.Name} for update: {failureMessage}");
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

    public async Task Update(string name, bool resolveDependencies) {
        Mod mod;
        
        lock (loadedMods) {
            if (!loadedMods.TryGetMod(name, out mod)) {
                Console.WriteLine($"{name} not found");

                return;
            }
        }
        
        if (!(await client.GetLatestModInfo(mod.Repository))
            .TryGetValue(out var latestMod, out string failureMessage))
            Console.WriteLine($"Failed to check {mod.Name} for update: {failureMessage}");
        else if (!resolveDependencies) {
            if (mod.Version >= latestMod.Version)
                Console.WriteLine($"{mod} is up to date");
            else
                await PerformDownload(mod.Repository);

            return;
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
                    await PerformDownload(mod.Repository);
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

    private async Task PerformDownload(Repository repository) {
        if (!(await client.DownloadMod(repository, gameDirectory))
            .TryGetValue(out var mod, out string failureMessage)) {
            Console.WriteLine($"Failed to download mod at {repository}: {failureMessage}");
            
            return;
        }

        lock (loadedMods)
            loadedMods.SetMod(mod);

        Console.WriteLine($"Successfully downloaded {mod}");
    }
    
    private async Task<bool> PerformCheckForUpdate(Mod mod) {
        if (!(await client.GetLatestModInfo(mod.Repository))
            .TryGetValue(out var latestMod, out string failureMessage))
            Console.WriteLine($"Failed to check {mod} for update: {failureMessage}");
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
        if (!(await client.GetLatestModInfo(mod.Repository))
            .TryGetValue(out var latestMod, out string failureMessage)) {
            Console.WriteLine($"Failed to check {mod.Name} for update: {failureMessage}");

            return false;
        }
        
        return mod.Version >= latestMod.Version;
    }
}