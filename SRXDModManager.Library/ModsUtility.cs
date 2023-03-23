using System.Collections.Generic;
using System.IO;

namespace SRXDModManager.Library; 

public static class ModsUtility {
    public static bool TryGetModFromDirectory(string directory, out Mod mod) {
        string path = Path.Combine(directory.Trim(), "manifest.json");
        
        if (!File.Exists(path)) {
            mod = null;
            
            return false;
        }

        return Util.DeserializeModManifest(File.ReadAllText(path))
            .Then(Util.CreateModFromManifest)
            .TryGetValue(out mod, out _);
    }

    public static IReadOnlyList<ModDependency> GetMissingDependencies(Mod mod, ModCollection mods) => GetMissingDependencies(new[] { mod }, mods);

    public static IReadOnlyList<ModDependency> GetMissingDependencies(IEnumerable<Mod> modsToCheck, ModCollection mods) {
        var missing = new Dictionary<string, ModDependency>();
        
        foreach (var mod in modsToCheck)
            AddMissingDependencies(missing, mod, mods);
        
        return new List<ModDependency>(missing.Values);
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
                .Then(directory => Util.VerifyFileExists(Path.Combine(directory, "UnityPlayer'dll")))
                .TryGetValue(out string directory, out string failureMessage))
            return Result<ActiveBuild>.Failure(failureMessage);

        bool monoExists = File.Exists(Path.Combine(directory, "UnityPlayer_Mono.dll"));
        bool il2CppExists = File.Exists(Path.Combine(directory, "UnityPlayer_IL2CPP.dll"));

        if (!il2CppExists && monoExists)
            return Result<ActiveBuild>.Success(ActiveBuild.Il2Cpp);
        
        if (!monoExists && il2CppExists)
            return Result<ActiveBuild>.Success(ActiveBuild.Mono);

        return Result<ActiveBuild>.Failure("Active build cannot be determined");
    }

    private static bool IsDependencyMissing(ModDependency dependency, ModCollection mods, Dictionary<string, ModDependency> missing) =>
        (!mods.TryGetMod(dependency.Name, out var foundMod) || dependency.Version > foundMod.Version)
        && (!missing.TryGetValue(dependency.Name, out var existingDependency) || dependency.Version > existingDependency.Version);

    private static void AddMissingDependencies(Dictionary<string, ModDependency> missing, Mod mod, ModCollection mods) {
        foreach (var dependency in mod.Dependencies) {
            if (IsDependencyMissing(dependency, mods, missing))
                missing.Add(dependency.Name, dependency);
        }
    }
}