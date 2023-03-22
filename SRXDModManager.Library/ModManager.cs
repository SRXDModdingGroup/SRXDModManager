using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SRXDModManager.Library; 

public class ModManager {
    public string GameDirectory { get; private set; }

    private string pluginsDirectory;
    private GitHubClient gitHubClient;
    private SortedDictionary<string, Mod> loadedMods;
    private ConcurrentQueue<DownloadRequest> requestQueue;

    public ModManager(string gameDirectory) {
        GameDirectory = gameDirectory;
        pluginsDirectory = Path.Combine(GameDirectory, "BepInEx", "plugins");
        gitHubClient = new GitHubClient();
        loadedMods = new SortedDictionary<string, Mod>();
        requestQueue = new ConcurrentQueue<DownloadRequest>();
    }

    public void ChangeGameDirectory(string gameDirectory) {
        if (gameDirectory == GameDirectory)
            return;
        
        GameDirectory = gameDirectory;
        pluginsDirectory = Path.Combine(GameDirectory, "BepInEx", "plugins");
        
        lock (loadedMods)
            loadedMods.Clear();
    }

    public bool TryGetMod(string name, out Mod mod) {
        lock (loadedMods)
            return loadedMods.TryGetValue(name, out mod);
    }

    public IReadOnlyList<Mod> GetLoadedMods() {
        lock (loadedMods)
            return new List<Mod>(loadedMods.Values);
    }

    public IReadOnlyList<ModDependency> GetMissingDependencies(Mod mod) {
        lock (loadedMods)
            return new List<ModDependency>(GetMissingDependencies(mod, loadedMods).Values);
    }

    public Result<SetActiveBuildResult> SetActiveBuild(ActiveBuild build) {
        if (!GetActiveBuild().TryGetValue(out var activeBuild, out string failureMessage))
            return Result<SetActiveBuildResult>.Failure(failureMessage);

        if (build == activeBuild)
            return Result<SetActiveBuildResult>.Success(SetActiveBuildResult.AlreadyActiveBuild);

        string activePlayerPath = Path.Combine(GameDirectory, "UnityPlayer.dll");
        string tempPlayerPath = Path.Combine(GameDirectory, "UnityPlayer.dll.tmp");
        string il2CppPlayerPath = Path.Combine(GameDirectory, "UnityPlayer_IL2CPP.dll");
        string monoPlayerPath = Path.Combine(GameDirectory, "UnityPlayer_Mono.dll");
        
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
            return Result<SetActiveBuildResult>.Failure(e.Message);
        }
        finally {
            if (File.Exists(tempPlayerPath))
                File.Delete(tempPlayerPath);
        }
        
        return Result<SetActiveBuildResult>.Success(SetActiveBuildResult.Success);
    }

    public Result<ActiveBuild> GetActiveBuild() {
        if (!VerifyGameDirectoryExists().Then(VerifyUnityPlayerExists).TryGetValue(out string failureMessage))
            return Result<ActiveBuild>.Failure(failureMessage);

        bool monoExists = File.Exists(Path.Combine(GameDirectory, "UnityPlayer_Mono.dll"));
        bool il2CppExists = File.Exists(Path.Combine(GameDirectory, "UnityPlayer_IL2CPP.dll"));

        if (!il2CppExists && monoExists)
            return Result<ActiveBuild>.Success(ActiveBuild.Il2Cpp);
        
        if (!monoExists && il2CppExists)
            return Result<ActiveBuild>.Success(ActiveBuild.Mono);

        return Result<ActiveBuild>.Failure("Active build cannot be determined");
    }

    public Result<IReadOnlyList<Mod>> RefreshLoadedMods() {
        lock (loadedMods) {
            loadedMods.Clear();

            if (!VerifyGameDirectoryExists()
                    .Then(VerifyPluginsDirectoryExists)
                    .TryGetValue(out string failureMessage))
                return Result<IReadOnlyList<Mod>>.Failure(failureMessage);

            foreach (string directory in Directory.GetDirectories(pluginsDirectory)) {
                string manifestPath = Path.Combine(directory, "manifest.json");

                if (!File.Exists(manifestPath)
                    || !Util.DeserializeModManifest(File.ReadAllText(manifestPath))
                        .Then(Util.CreateModFromManifest)
                        .TryGetValue(out var mod, out _))
                    continue;

                loadedMods[mod.Name] = mod;
            }

            return Result<IReadOnlyList<Mod>>.Success(new List<Mod>(loadedMods.Values));
        }
    }

    public async Task<Result<Mod>> DownloadMod(Repository repository) {
        var request = new DownloadRequest(repository, pluginsDirectory, gitHubClient);
        
        requestQueue.Enqueue(request);
        
        return await request.GetResult();
    }

    public async Task<Result<Mod>> GetLatestModInfo(Repository repository) {
        if (!(await gitHubClient.GetLatestRelease(repository))
            .Then(Util.GetManifestAsset)
            .TryGetValue(out var asset, out string failureMessage)
            || !(await gitHubClient.DownloadAssetAsString(asset))
                .Then(Util.DeserializeModManifest)
                .Then(Util.CreateModFromManifest)
                .TryGetValue(out var mod, out failureMessage))
            return Result<Mod>.Failure(failureMessage);
        
        return Result<Mod>.Success(mod);
    }

    private Result VerifyGameDirectoryExists() {
        if (!Directory.Exists(GameDirectory))
            return Result.Failure($"Could not find game directory {GameDirectory}");
        
        return Result.Success();
    }
    
    private Result VerifyPluginsDirectoryExists() {
        if (!Directory.Exists(pluginsDirectory))
            return Result.Failure($"Could not find plugins directory {pluginsDirectory}. Ensure that BepInEx is installed");
        
        return Result.Success();
    }

    private Result VerifyUnityPlayerExists() {
        if (!File.Exists(Path.Combine(GameDirectory, "UnityPlayer.dll")))
            return Result.Failure("Could not find UnityPlayer.dll");
        
        return Result.Success();
    }
    
    private static Dictionary<string, ModDependency> GetMissingDependencies(Mod mod, IReadOnlyDictionary<string, Mod> mods) {
        var missing = new Dictionary<string, ModDependency>();
        
        AddMissingDependencies(missing, mod, mods);

        return missing;
    }
    
    private static void AddMissingDependencies(Dictionary<string, ModDependency> missing, Mod mod, IReadOnlyDictionary<string, Mod> mods) {
        foreach (var dependency in mod.Dependencies) {
            if ((!mods.TryGetValue(dependency.Name, out var foundMod) || dependency.Version > foundMod.Version)
                && (!missing.TryGetValue(dependency.Name, out var existingDependency) || dependency.Version > existingDependency.Version))
                missing.Add(dependency.Name, dependency);
        }
    }
}