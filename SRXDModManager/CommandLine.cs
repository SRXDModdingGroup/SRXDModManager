﻿using System;
using System.CommandLine;
using SRXDModManager.Library;

namespace SRXDModManager; 

public class CommandLine {
    private ModManager modManager;
    private RootCommand root;

    public CommandLine(ModManager modManager) {
        this.modManager = modManager;
        root = new RootCommand();
        
        CreateCommands();
    }

    public void Invoke(string[] args) => root.Invoke(args);

    private void CreateCommands() {
        root.AddCommand(CreateCommand("bepinex", "Installs or uninstalls BepInEx", command => {
            command.AddCommand(CreateCommand("install", "Installs the latest non-preview version of BepInEx",
                command => command.SetHandler(() => modManager.InstallBepInEx().Wait())));
            command.AddCommand(CreateCommand("uninstall", "Uninstalls BepInEx", command => {
                var fullOption = new Option<bool>(new[] { "--full", "-f" }, "Delete all BepInEx files, including plugins and config");
                
                command.AddOption(fullOption);
                command.SetHandler(modManager.UninstallBepInEx, fullOption);
            }));
        }));
        
        root.AddCommand(CreateCommand("build", "Sets which build of the game to use", command => {
            command.AddCommand(CreateCommand("il2cpp", "The IL2CPP build. Mods will not be loaded when using this build",
                command => command.SetHandler(() => modManager.SwitchBuild(ActiveBuild.Il2Cpp))));
            command.AddCommand(CreateCommand("mono", "The Mono build. Mods will be loaded when using this build",
                command => command.SetHandler(() => modManager.SwitchBuild(ActiveBuild.Mono))));
        }));
        
        root.AddCommand(CreateCommand("check", "Checks a mod for updates or missing dependencies", command => {
            var nameArg = new Argument<string>("name", "The name of the mod");
            
            command.AddCommand(CreateCommand("all", "Checks all loaded mods for updates or missing dependencies",
                command => command.SetHandler(modManager.CheckAllForUpdates)));
            command.AddArgument(nameArg);
            command.SetHandler(name => modManager.CheckForUpdate(name).Wait(), nameArg);
        }));

        root.AddCommand(CreateCommand("download", "Downloads a mod from a Git release", command => {
            var repositoryArg = new Argument<string>("repository",
                "The repository from which to get the latest release. If the repository is owned by SRXDModdingGroup, " +
                "you only need to specify the repository name. If the repository is under a different group, specify " +
                "both the owner and name, separated by a slash (/)");
            var dependenciesOption = new Option<bool>(new[] { "--dependencies", "-d" }, "Also download missing dependencies");
            
            command.AddArgument(repositoryArg);
            command.AddOption(dependenciesOption);
            command.SetHandler((repository, resolveDependencies) => modManager.Download(repository, resolveDependencies).Wait(), repositoryArg, dependenciesOption);
        }));
        
        root.AddCommand(CreateCommand("exit", "Exits the application"));
        
        root.AddCommand(CreateCommand("info", "Gets detailed information about a mod", command => {
            var nameArg = new Argument<string>("name", "The name of the mod");
            
            command.AddCommand(CreateCommand("all", "Gets info for all loaded mods",
                command => command.SetHandler(modManager.GetAllModInfo)));
            command.AddArgument(nameArg);
            command.SetHandler(modManager.GetModInfo, nameArg);
        }));
        
        root.AddCommand(CreateCommand("refresh", "Refreshes the list of downloaded mods",
            command => { command.SetHandler(modManager.RefreshMods); }));
        
        root.AddCommand(CreateCommand("update", "Updates a mod if there is a new version available", command => {
            var nameArg = new Argument<string>("name", "The name of the mod");
            var dependenciesOption = new Option<bool>(new[] { "--dependencies", "-d" }, "Also download missing dependencies");
            
            command.AddCommand(CreateCommand("all", "Updates all loaded mods", command => {
                command.AddOption(dependenciesOption);
                command.SetHandler(resolveDependencies => modManager.UpdateAll(resolveDependencies).Wait(), dependenciesOption);
            }));
            
            command.AddArgument(nameArg);
            command.AddOption(dependenciesOption);
            command.SetHandler((name, resolveDependencies) => modManager.Update(name, resolveDependencies).Wait(), nameArg, dependenciesOption);
        }));
    }
    
    private static Command CreateCommand(string name, string description, Action<Command> init = null) {
        var command = new Command(name, description);

        init?.Invoke(command);
        
        return command;
    }
}