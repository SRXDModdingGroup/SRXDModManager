using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SRXDModManager.Library;

namespace SRXDModManager; 

public class CommandLine {
    private ModManager modManager;
    private RootCommand root;

    public CommandLine(ModManager modManager) {
        this.modManager = modManager;
        root = new RootCommand();
        
        root.AddCommand(CreateCommand("build", "Sets which build of the game to use", command => {
            command.AddCommand(CreateCommand("il2cpp", "The IL2CPP build. Mods will not be loaded when using this build",
                command => command.SetHandler(() => SwitchBuild(ActiveBuild.Il2Cpp))));
            command.AddCommand(CreateCommand("mono", "The Mono build. Mods will be loaded when using this build",
                command => command.SetHandler(() => SwitchBuild(ActiveBuild.Mono))));
        }));
        
        root.AddCommand(CreateCommand("check", "Checks a mod for updates or missing dependencies", command => {
            var nameArg = new Argument<string>("name", "The name of the mod");
            
            command.AddCommand(CreateCommand("all", "Checks all loaded mods for updates or missing dependencies", command => command.SetHandler(CheckAllForUpdates)));
            command.AddArgument(nameArg);
            command.SetHandler(CheckForUpdate, nameArg);
        }));

        root.AddCommand(CreateCommand("download", "Downloads a mod from a Git release", command => {
            var repositoryArg = new Argument<string>("repository",
                "The repository from which to get the latest release. If the repository is owned by SRXDModdingGroup, " +
                "you only need to specify the repository name. If the repository is under a different group, specify " +
                "both the owner and name, separated by a slash (/)");
            var dependenciesOption = new Option<bool>(new[] { "--dependencies", "-d" }, "Also download missing dependencies");
            
            command.AddArgument(repositoryArg);
            command.AddOption(dependenciesOption);
            command.SetHandler(DownloadMod, repositoryArg, dependenciesOption);
        }));
        
        root.AddCommand(CreateCommand("exit", "Exits the application"));
        
        root.AddCommand(CreateCommand("info", "Gets detailed information about a mod", command => {
            var nameArg = new Argument<string>("name", "The name of the mod");
            
            command.AddCommand(CreateCommand("all", "Gets info for all loaded mods", command => command.SetHandler(GetAllModInfo)));
            command.AddArgument(nameArg);
            command.SetHandler(GetModInfo, nameArg);
        }));
        
        root.AddCommand(CreateCommand("refresh", "Refreshes the list of downloaded mods", command => { command.SetHandler(RefreshMods); }));
        
        root.AddCommand(CreateCommand("update", "Updates a mod if there is a new version available", command => {
            var nameArg = new Argument<string>("name", "The name of the mod");
            var dependenciesOption = new Option<bool>(new[] { "--dependencies", "-d" }, "Also download missing dependencies");
            
            command.AddCommand(CreateCommand("all", "Updates all loaded mods", command => {
                command.AddOption(dependenciesOption);
                command.SetHandler(UpdateAllMods, dependenciesOption);
            }));
            
            command.AddArgument(nameArg);
            command.AddOption(dependenciesOption);
            command.SetHandler(UpdateMod, nameArg, dependenciesOption);
        }));
    }

    public void Invoke(string[] args) => root.Invoke(args);

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
            tasks[i] = CheckOne(mods[i]);

        Task.WaitAll(tasks);

        if (!tasks.Any(task => task.Result))
            Console.WriteLine("All mods are up to date");
        
        async Task<bool> CheckOne(Mod mod) {
            if (!(await modManager.GetLatestVersion(mod)).TryGetValue(out var latestVersion, out string failureMessage))
                Console.WriteLine($"Failed to check {mod} for update: {failureMessage}");
            else if (latestVersion > mod.Version)
                Console.WriteLine($"{mod} is not up to date. Latest version is {latestVersion}");
            else if (modManager.GetMissingDependencies(mod).Count > 0)
                Console.WriteLine($"{mod} is missing dependencies");
            else
                return false;

            return true;
        }
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
        
        bool needsUpdate = PerformCheckForUpdate(mod).Result;

        if (!needsUpdate)
            return;
        
        var queue = new DownloadQueue(modManager);
            
        queue.Enqueue(new DownloadRequest(mod.Repository, resolveDependencies));
        queue.WaitAll();
    }

    public void UpdateAllMods(bool resolveDependencies) {
        var mods = modManager.GetLoadedMods();
        var updateChecks = new Task<bool>[mods.Count];

        for (int i = 0; i < updateChecks.Length; i++)
            updateChecks[i] = PerformCheckForUpdate(mods[i]);

        var requests = new List<DownloadRequest>();

        for (int i = 0; i < updateChecks.Length; i++) {
            if (updateChecks[i].Result)
                requests.Add(new DownloadRequest(mods[i].Repository, resolveDependencies));
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
        else if (mod.Version >= latestVersion)
            Console.WriteLine($"{mod} is up to date");
        else
            return true;

        return false;
    }

    private static Command CreateCommand(string name, string description, Action<Command> init = null) {
        var command = new Command(name, description);

        init?.Invoke(command);
        
        return command;
    }
}