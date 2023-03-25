using System.Collections.Generic;
using System.IO;

namespace SRXDModManager.Library; 

public static class ModsUtility {
    public static DependencyCollection GetDependencies(IEnumerable<Mod> mods) {
        var dependencies = new DependencyCollection();

        foreach (var mod in mods) {
            foreach (var dependency in mod.Dependencies)
                dependencies.TryAddDependency(dependency);
        }

        return dependencies;
    }
    
    public static DependencyCollection GetMissingDependencies(Mod mod, ModCollection modCollection) {
        var missingDependencies = new DependencyCollection();

        foreach (var dependency in mod.Dependencies) {
            if (!modCollection.ContainsMod(dependency.Name, dependency.Version))
                missingDependencies.TryAddDependency(dependency);
        }

        return missingDependencies;
    }

    public static DependencyCollection GetMissingDependencies(IEnumerable<Mod> mods, ModCollection modCollection) {
        var missingDependencies = new DependencyCollection();

        foreach (var mod in mods) {
            foreach (var dependency in mod.Dependencies) {
                if (!modCollection.ContainsMod(dependency.Name, dependency.Version))
                    missingDependencies.TryAddDependency(dependency);
            }
        }

        return missingDependencies;
    }

    public static Result<bool> TrySetActiveBuild(ActiveBuild build, string gameDirectory) {
        if (!GetActiveBuild(gameDirectory)
                .TryGetValue(out var activeBuild, out string failureMessage))
            return Result<bool>.Failure(failureMessage);

        if (build == activeBuild)
            return Result<bool>.Success(false);

        string activePlayerPath = Path.Combine(gameDirectory, "UnityPlayer.dll");
        string tempPlayerPath = Path.Combine(gameDirectory, "UnityPlayer.dll.tmp");
        string il2CppPlayerPath = Path.Combine(gameDirectory, "UnityPlayer_IL2CPP.dll");
        string monoPlayerPath = Path.Combine(gameDirectory, "UnityPlayer_Mono.dll");
        
        File.Move(activePlayerPath, tempPlayerPath);

        try {
            switch (build) {
                case ActiveBuild.Il2Cpp:
                    File.Move(il2CppPlayerPath, activePlayerPath);
                    File.Move(tempPlayerPath, monoPlayerPath);
                    break;
                case ActiveBuild.Mono:
                    File.Move(monoPlayerPath, activePlayerPath);
                    File.Move(tempPlayerPath, il2CppPlayerPath);
                    break;
            }
        }
        catch (IOException e) {
            return Result<bool>.Failure(e.Message);
        }
        finally {
            if (File.Exists(tempPlayerPath))
                File.Delete(tempPlayerPath);
        }
        
        return Result<bool>.Success(true);
    }

    public static Result<ActiveBuild> GetActiveBuild(string gameDirectory) {
        if (!Util.VerifyDirectoryExists(gameDirectory)
                .Then(directory => Util.VerifyFileExists(Path.Combine(directory, "UnityPlayer.dll")))
                .TryGetValue(out _, out string failureMessage))
            return Result<ActiveBuild>.Failure(failureMessage);

        bool monoExists = File.Exists(Path.Combine(gameDirectory, "UnityPlayer_Mono.dll"));
        bool il2CppExists = File.Exists(Path.Combine(gameDirectory, "UnityPlayer_IL2CPP.dll"));

        if (!il2CppExists && monoExists)
            return Result<ActiveBuild>.Success(ActiveBuild.Il2Cpp);
        
        if (!monoExists && il2CppExists)
            return Result<ActiveBuild>.Success(ActiveBuild.Mono);

        return Result<ActiveBuild>.Failure("Active build cannot be determined");
    }

    public static Result<(bool exists, Mod mod)> TryGetModFromDirectory(string directory, string name) {
        if (!Util.VerifyDirectoryExists(directory)
                .TryGetValue(out directory, out string failureMessage))
            return Result<(bool, Mod)>.Failure(failureMessage);

        string path = Path.Combine(directory, name.Trim(), "manifest.json");
        
        if (!File.Exists(path))
            return Result<(bool, Mod)>.Success((false, null));

        if (!Util.DeserializeModManifest(File.ReadAllText(path))
                .Then(Util.CreateModFromManifest)
                .TryGetValue(out var mod, out  failureMessage))
            return Result<(bool, Mod)>.Failure(failureMessage);
        
        return Result<(bool, Mod)>.Success((true, mod));
    }

    public static Result<IReadOnlyList<Mod>> GetAllModsInDirectory(string directory) {
        if (!Util.VerifyDirectoryExists(directory)
                .TryGetValue(out directory, out string failureMessage))
            return Result<IReadOnlyList<Mod>>.Failure(failureMessage);

        var mods = new List<Mod>();

        foreach (string subDirectory in Directory.GetDirectories(directory)) {
            string path = Path.Combine(subDirectory, "manifest.json");

            if (File.Exists(path) && Util.DeserializeModManifest(File.ReadAllText(path))
                    .Then(Util.CreateModFromManifest)
                    .TryGetValue(out var mod, out _))
                mods.Add(mod);
        }

        return Result<IReadOnlyList<Mod>>.Success(mods);
    }
}