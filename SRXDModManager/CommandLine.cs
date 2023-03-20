using System;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
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
        
        root.AddCommand(CreateCommand("check", "Checks a mod for updates", command => {
            var nameArg = new Argument<string>("name", "The name of the mod");
            
            command.AddCommand(CreateCommand("all", "Checks all loaded mods for updates", command => command.SetHandler(CheckAllForUpdates)));
            command.AddArgument(nameArg);
            command.SetHandler(CheckForUpdate, nameArg);
        }));

        root.AddCommand(CreateCommand("download", "Downloads a mod from a Git release", command => {
            var repositoryArg = new Argument<string>("repository",
                "The repository from which to get the latest release. If the repository is owned by SRXDModdingGroup, " +
                "you only need to specify the repository name. If the repository is under a different group, specify " +
                "both the owner and name, separated by a slash (/)");
            
            command.AddArgument(repositoryArg);
            command.SetHandler(DownloadMod, repositoryArg);
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
            
            command.AddCommand(CreateCommand("all", "Updates all loaded mods", command => command.SetHandler(UpdateAllMods)));
            command.AddArgument(nameArg);
            command.SetHandler(UpdateMod, nameArg);
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
        else
            Console.WriteLine($"{mod} is up to date");
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
            else
                return false;

            return true;
        }
    }

    public void DownloadMod(string repository) {
        if (!repository.Contains("/"))
            repository = "SRXDModdingGroup/" + repository;
                
        if (modManager.DownloadMod(repository).Result.TryGetValue(out var mod, out string failureMessage))
            Console.WriteLine($"Successfully downloaded {mod}");
        else
            Console.WriteLine($"Failed to download mod at {repository}: {failureMessage}");
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

    public void UpdateMod(string name) {
        if (!modManager.TryGetMod(name, out var mod)) {
            Console.WriteLine($"{name} not found");
            
            return;
        }

        if (!modManager.GetLatestVersion(mod).Result.TryGetValue(out var latestVersion, out string failureMessage))
            Console.WriteLine($"Failed to check {mod} for update: {failureMessage}");
        else if (latestVersion > mod.Version)
            DownloadMod(mod.Repository);
        else
            Console.WriteLine($"{mod} is up to date");
    }

    public void UpdateAllMods() {
        var mods = modManager.GetLoadedMods();
        var tasks = new Task<bool>[mods.Count];

        for (int i = 0; i < mods.Count; i++)
            tasks[i] = UpdateOne(mods[i]);

        Task.WaitAll(tasks);

        if (!tasks.Any(task => task.Result))
            Console.WriteLine("All mods are up to date");

        async Task<bool> UpdateOne(Mod mod) {
            if (!(await modManager.GetLatestVersion(mod)).TryGetValue(out var latestVersion, out string failureMessage))
                Console.WriteLine($"Failed to check {mod} for update: {failureMessage}");
            else if (mod.Version >= latestVersion)
                return false;
            else if (!(await modManager.DownloadMod(mod.Repository)).TryGetValue(out var newMod, out failureMessage))
                Console.WriteLine($"Failed to update {mod}: {failureMessage}");
            else
                Console.WriteLine($"Successfully downloaded {newMod}");

            return true;
        }
    }
    
    private static Command CreateCommand(string name, string description, Action<Command> init = null) {
        var command = new Command(name, description);

        init?.Invoke(command);
        
        return command;
    }
}