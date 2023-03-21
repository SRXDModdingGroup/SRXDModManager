using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SRXDModManager.Library;

namespace SRXDModManager; 

public class Actions {
    private ModManager modManager;

    public Actions(ModManager modManager) {
        this.modManager = modManager;
    }
    
    public void SwitchBuild(ActiveBuild build) {
        if (!modManager.GetActiveBuild().TryGetValue(out var activeBuild, out string failureMessage))
            Console.WriteLine($"Failed to get active build: {failureMessage}");
        else if (activeBuild == build)
            Console.WriteLine($"{build} is already the active build");
        else if (modManager.SetActiveBuild(build).TryGetValue(out failureMessage))
            Console.WriteLine($"Successfully switched builds to {build}");
        else
            Console.WriteLine($"Failed to switch build to {build}: {failureMessage}");
    }

    public void CheckForUpdate(string name) {
        if (!modManager.TryGetMod(name, out var mod))
            Console.WriteLine($"{name} not found");
        else if (!modManager.GetLatestVersion(mod).Result.TryGetValue(out var latestVersion, out string failureMessage))
            Console.WriteLine($"Failed to check {mod.Name} for update: {failureMessage}");
        else if (latestVersion > mod.Version)
            Console.WriteLine($"{mod} is not up to date. Latest version is {latestVersion}");
        else {
            var missingDependencies = modManager.GetMissingDependencies(mod);

            if (missingDependencies.Count > 0) {
                Console.WriteLine($"{mod} is missing dependencies:");

                foreach (var dependency in missingDependencies)
                    Console.WriteLine(dependency);
            }
            else
                Console.WriteLine($"{mod} is up to date");
        }
    }

    public void CheckAllForUpdates() {
        var mods = modManager.GetLoadedMods();
        var tasks = new Task<bool>[mods.Count];

        for (int i = 0; i < mods.Count; i++)
            tasks[i] = PerformCheckForUpdate(mods[i]);

        Task.WaitAll(tasks);

        if (!tasks.Any(task => task.Result))
            Console.WriteLine("All mods are up to date");
    }

    public void DownloadMod(string repository, bool resolveDependencies) {
        if (!repository.Contains("/"))
            repository = "SRXDModdingGroup/" + repository;

        var queue = new DownloadQueue(modManager);
        
        queue.Enqueue(new DownloadRequest(repository, resolveDependencies));
        queue.WaitAll();
    }

    public void GetModInfo(string name) {
        if (!modManager.TryGetMod(name, out var mod))
            Console.WriteLine($"{name} not found");
        else
            Console.WriteLine($"{mod}: {mod.Description}");
    }

    public void GetAllModInfo() {
        var mods = modManager.GetLoadedMods();
        
        if (mods.Count == 0)
            Console.WriteLine("No mods found");
        else {
            foreach (var mod in mods)
                Console.WriteLine($"{mod}: {mod.Description}");
        }
    }

    public void RefreshMods() {
        string configPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config.json");
        string gameDirectory = null;

        if (File.Exists(configPath)) {
            try {
                var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));

                gameDirectory = config.GameDirectory;
                Console.WriteLine($"Using game directory {gameDirectory}");
            }
            catch (JsonException) {
                Console.WriteLine("Failed to deserialize config.json");
            }
        }
        else
            Console.WriteLine("config.json not found");

        if (gameDirectory == null) {
            Console.WriteLine("Defaulting game directory to C:\\Program Files (x86)\\Steam\\steamapps\\common\\Spin Rhythm");
            gameDirectory = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Spin Rhythm";
        }

        modManager.ChangeGameDirectory(gameDirectory);

        if (!modManager.RefreshLoadedMods().TryGetValue(out var mods, out string failureMessage)) {
            Console.WriteLine($"Failed to load mods: {failureMessage}");
            
            return;
        }

        if (mods.Count == 0)
            Console.WriteLine("No mods found");
        else {
            Console.WriteLine($"Found {mods.Count} mod{(mods.Count > 1 ? "s" : string.Empty)}:");
            
            foreach (var mod in mods)
                Console.WriteLine(mod);
        }
    }

    public void UpdateMod(string name, bool resolveDependencies) {
        if (!modManager.TryGetMod(name, out var mod)) {
            Console.WriteLine($"{name} not found");
            
            return;
        }
        
        var requests = PerformGetUpdate(mod, resolveDependencies).Result;

        if (requests.Length == 0)
            return;
        
        var queue = new DownloadQueue(modManager);

        foreach (var request in requests)
            queue.Enqueue(request);

        queue.WaitAll();
    }

    public void UpdateAllMods(bool resolveDependencies) {
        var mods = modManager.GetLoadedMods();
        var updateChecks = new Task<DownloadRequest[]>[mods.Count];

        for (int i = 0; i < updateChecks.Length; i++)
            updateChecks[i] = PerformGetUpdate(mods[i], resolveDependencies);

        var requests = new List<DownloadRequest>();

        foreach (var task in updateChecks) {
            foreach (var request in task.Result)
                requests.Add(request);
        }

        if (requests.Count == 0)
            return;
        
        var queue = new DownloadQueue(modManager);

        foreach (var request in requests)
            queue.Enqueue(request);

        queue.WaitAll();
    }
    
    private async Task<bool> PerformCheckForUpdate(Mod mod) {
        if (!(await modManager.GetLatestVersion(mod)).TryGetValue(out var latestVersion, out string failureMessage))
            Console.WriteLine($"Failed to check {mod} for update: {failureMessage}");
        else if (latestVersion > mod.Version)
            Console.WriteLine($"{mod} is not up to date. Latest version is {latestVersion}");
        else if (modManager.GetMissingDependencies(mod).Count > 0)
            Console.WriteLine($"{mod} is missing dependencies");
        else {
            Console.WriteLine($"{mod} is up to date");
            
            return false;
        }

        return true;
    }

    private async Task<DownloadRequest[]> PerformGetUpdate(Mod mod, bool resolveDependencies) {
        if (!(await modManager.GetLatestVersion(mod)).TryGetValue(out var latestVersion, out string failureMessage)) {
            Console.WriteLine($"Failed to check {mod} for update: {failureMessage}");
            
            return Array.Empty<DownloadRequest>();
        }

        if (mod.Version < latestVersion)
            return new[] { new DownloadRequest(mod.Repository, resolveDependencies) };

        if (!resolveDependencies) {
            Console.WriteLine($"{mod} is up to date");

            return Array.Empty<DownloadRequest>();
        }

        var missingDependencies = modManager.GetMissingDependencies(mod);

        if (missingDependencies.Count == 0)
            return Array.Empty<DownloadRequest>();
            
        var requests = new DownloadRequest[missingDependencies.Count];

        for (int i = 0; i < missingDependencies.Count; i++)
            requests[i] = new DownloadRequest(missingDependencies[i].Repository, true);

        return requests;
    }
}