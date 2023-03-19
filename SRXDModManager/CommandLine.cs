using System;
using System.CommandLine;
using SRXDModManager.Library;

namespace SRXDModManager; 

public class CommandLine {
    private ModManager modManager;
    private RootCommand root;

    private CommandLine(ModManager modManager) {
        this.modManager = modManager;
        root = new RootCommand();
    }

    public void Invoke(string[] args) => root.Invoke(args);

    private void Init() {
        root.AddCommand(CreateCommand("build", "Sets which build of the game to use", command => {
            command.AddCommand(CreateCommand("il2cpp", "The il2cpp build. Mods will not be loaded when using this build",
                command => command.SetHandler(() => SwitchBuild(ActiveBuild.Il2Cpp))));
            command.AddCommand(CreateCommand("mono", "The mono build. Mods will be loaded when using this build",
                command => command.SetHandler(() => SwitchBuild(ActiveBuild.Mono))));
        }));

        root.AddCommand(CreateCommand("download", "Downloads a mod from a Git release", command => {
            var repositoryArg = new Argument<string>("repository",
                "The repository from which to get the latest release. If the repository is owned by SRXDModdingGroup, " +
                "you only need to specify the repository name. If the repository is under a different group, specify " +
                "both the owner and name, separated by a slash (/)");
            
            command.AddArgument(repositoryArg);
            command.SetHandler(DownloadMod, repositoryArg);
        }));
        
        root.AddCommand(CreateCommand("refresh", "Refreshes the list of downloaded mods", command => {
            command.SetHandler(RefreshMods);
        }));
    }

    public void SwitchBuild(ActiveBuild build) {
        if (modManager.GetActiveBuild() == build)
            Console.WriteLine("Build is already active");
        else if (modManager.SetActiveBuild(build).TryGetValue(out string failureMessage))
            Console.WriteLine("Successfully switched builds");
        else
            Console.WriteLine($"Failed to switch builds: {failureMessage}");
    }

    public void DownloadMod(string repository) {
        if (!repository.Contains("/"))
            repository = "SRXDModdingGroup/" + repository;
                
        if (modManager.DownloadMod(repository).Result.TryGetValue(out _, out string failureMessage))
            Console.WriteLine($"Successfully downloaded mod at {repository}");
        else
            Console.WriteLine($"Failed to download mod at {repository}: {failureMessage}");
    }

    public void RefreshMods() {
        if (!modManager.RefreshLoadedMods().TryGetValue(out var mods, out string failureMessage)) {
            Console.WriteLine($"Failed to load mods: {failureMessage}");
            
            return;
        }

        if (mods.Count == 0)
            Console.WriteLine("No mods found");
        else {
            Console.WriteLine($"Successfully loaded {mods.Count} mod{(mods.Count > 1 ? "s" : string.Empty)}:");
            
            foreach (var mod in mods)
                Console.WriteLine($"{mod.Name} {mod.Version}");
        }
    }

    public static CommandLine Create(ModManager modManager) {
        var commandLine = new CommandLine(modManager);
        
        commandLine.Init();

        return commandLine;
    }
    
    private static Command CreateCommand(string name, string description, Action<Command> init = null) {
        var command = new Command(name, description);

        init?.Invoke(command);
        
        return command;
    }
}