using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SRXDModManager.Library; 

public class ModManager {
    private string gameDirectory;
    private string pluginsDirectory;
    
    private GitHubClient gitHubClient;
    private JsonSerializer serializer;
    private SortedDictionary<string, Mod> loadedMods;

    public ModManager(string gameDirectory) {
        this.gameDirectory = gameDirectory;
        pluginsDirectory = Path.Combine(gameDirectory, "BepInEx", "plugins");

        gitHubClient = new GitHubClient();
        serializer = new JsonSerializer();
        loadedMods = new SortedDictionary<string, Mod>();
    }

    public bool TryGetMod(string name, out Mod mod) => loadedMods.TryGetValue(name, out mod);

    public ActiveBuild GetActiveBuild() {
        if (!File.Exists(Path.Combine(gameDirectory, "UnityPlayer.dll")))
            return ActiveBuild.Unknown;

        bool monoExists = File.Exists(Path.Combine(gameDirectory, "UnityPlayer_Mono.dll"));
        bool il2CppExists = File.Exists(Path.Combine(gameDirectory, "UnityPlayer_IL2CPP.dll"));

        if (!il2CppExists && monoExists)
            return ActiveBuild.Il2Cpp;
        
        if (!monoExists && il2CppExists)
            return ActiveBuild.Mono;

        return ActiveBuild.Unknown;
    }

    public Result SetActiveBuild(ActiveBuild build) {
        if (build != ActiveBuild.Il2Cpp && build != ActiveBuild.Mono)
            throw new ArgumentOutOfRangeException(nameof(build), "Must be Il2Cpp or Mono");
        
        var activeBuild = GetActiveBuild();

        if (activeBuild == ActiveBuild.Unknown)
            return Result.Failure("Current active build is unknown");
        
        if (build == activeBuild)
            return Result.Success();
        
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
        finally {
            if (File.Exists(tempPlayerPath))
                File.Delete(tempPlayerPath);
        }
        
        return Result.Success();
    }

    public Result<IReadOnlyList<Mod>> RefreshLoadedMods() {
        loadedMods.Clear();
        
        if (!Directory.Exists(pluginsDirectory))
            return Result<IReadOnlyList<Mod>>.Failure($"Directory \"{pluginsDirectory}\" was not found");
        
        foreach (string directory in Directory.GetDirectories(pluginsDirectory)) {
            string manifestPath = Path.Combine(directory, "manifest.json");
            
            if (!File.Exists(manifestPath))
                continue;

            using var reader = File.OpenText(manifestPath);
            var manifest = JsonConvert.DeserializeObject<ModManifest>(reader.ReadToEnd());
            
            if (manifest != null)
                loadedMods.Add(manifest.Name, new Mod(directory, manifest));
        }
        
        return Result<IReadOnlyList<Mod>>.Success(new List<Mod>(loadedMods.Values));
    }
    
    public Result VerifyDirectoriesExist() {
        if (!VerifyGameDirectoryExists()
                .Then(VerifyPluginsDirectoryExists)
                .TryGetValue(out string message))
            return Result.Failure(message);

        return Result.Success();
    }

    public IReadOnlyList<Mod> GetLoadedMods() => new List<Mod>(loadedMods.Values);

    public async Task<Result<bool>> NeedsUpdate(Mod mod) {
        if (!GetModVersion(mod)
                .TryGetValue(out var currentVersion, out string message)
            || !(await gitHubClient.GetLatestRelease(mod.Repository))
                .Then(GetReleaseVersion)
                .TryGetValue(out var latestVersion, out message))
            return Result<bool>.Failure(message);

        return Result<bool>.Success(latestVersion > currentVersion);
    }

    public async Task<Result<Mod>> DownloadMod(string repository) {
        if (!(await gitHubClient.GetLatestRelease(repository))
                .TryGetValue(out var release, out string message)
            || !GetReleaseVersion(release)
                .TryGetValue(out var expectedVersion, out message)
            || !GetZipAsset(release)
                .TryGetValue(out var zipAsset, out message)
            || !(await PerformDownload(zipAsset, expectedVersion, repository))
                .TryGetValue(out var mod, out message))
            return Result<Mod>.Failure(message);
        
        loadedMods[mod.Name] = mod;

        return Result<Mod>.Success(mod);
    }

    private Result VerifyGameDirectoryExists() {
        if (!File.Exists(gameDirectory))
            return Result.Failure($"Could not find game directory {gameDirectory}");
        
        return Result.Success();
    }
    
    private Result VerifyPluginsDirectoryExists() {
        if (!File.Exists(pluginsDirectory))
            return Result.Failure($"Could not find plugins directory {pluginsDirectory}. Ensure that BepInEx is installed");
        
        return Result.Success();
    }

    private Result<ModManifest> DeserializeModManifest(string path) {
        ModManifest manifest;
        
        using (var reader = new JsonTextReader(File.OpenText(path)))
            manifest = serializer.Deserialize<ModManifest>(reader);
        
        if (manifest == null)
            return Result<ModManifest>.Failure($"Could not deserialize manifest file for mod at {path}");
        
        return Result<ModManifest>.Success(manifest);
    }

    private async Task<Result<Mod>> PerformDownload(GitHubAsset zipAsset, Version expectedVersion, string repository) {
        var tempFiles = new List<string>();
        
        if (!(await gitHubClient.DownloadAsset(zipAsset))
            .TryGetValue(out var stream, out string message))
            return Result<Mod>.Failure(message);

        try {
            using (var archive = new ZipArchive(stream)) {
                foreach (var entry in archive.Entries) {
                    string path = Path.Combine(Path.GetTempPath(), entry.Name);

                    tempFiles.Add(path);

                    using var entryStream = entry.Open();
                    using var fileStream = File.OpenWrite(path);

                    await entryStream.CopyToAsync(fileStream);
                }
            }

            string manifestPath = Path.Combine(Path.GetTempPath(), "manifest.json");

            if (!tempFiles.Contains(manifestPath))
                return Result<Mod>.Failure($"Mod at {repository} does not have a manifest.json file");

            if (!DeserializeModManifest(manifestPath)
                    .Then(AssertModHasName)
                    .TryGetValue(out var manifest, out message)
                || !GetModVersion(manifest)
                    .Then(version => AssertVersionsEqual(version, expectedVersion, manifest.Name))
                    .TryGetValue(out _, out message))
                return Result<Mod>.Failure(message);

            string directory = Path.Combine(pluginsDirectory, manifest.Name);

            if (Directory.Exists(directory))
                Directory.Delete(directory, true);

            Directory.CreateDirectory(directory);

            foreach (string path in tempFiles)
                File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));

            return Result<Mod>.Success(new Mod(directory, manifest));
        }
        finally {
            foreach (string path in tempFiles) {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    private static bool TryParseVersion(string text, out Version version) {
        for (int i = text.Length - 1; i >= 0; i--) {
            char character = text[i];

            if (!char.IsDigit(character) && character != '.')
                return Version.TryParse(text.Substring(i + 1), out version);
        }

        return Version.TryParse(text, out version);
    }
    
    private static Result<GitHubAsset> GetZipAsset(GitHubRelease release) {
        foreach (var asset in release.Assets) {
            if (asset.Name == "plugin.zip")
                return Result<GitHubAsset>.Success(asset);
        }

        return Result<GitHubAsset>.Failure($"Release {release.Name} does not have a plugin.zip file");
    }

    private static Result<Version> GetReleaseVersion(GitHubRelease release) {
        if (!TryParseVersion(release.TagName, out var version))
            return Result<Version>.Failure($"Tag for the latest release of mod {release.Name} is not a valid version string");

        return Result<Version>.Success(version);
    }

    private static Result<Version> GetModVersion(ModManifest manifest) {
        if (!TryParseVersion(manifest.Version, out var version))
            return Result<Version>.Failure($"Manifest file for mod {manifest.Name} does not have a valid version");

        return Result<Version>.Success(version);;
    }

    private static Result<Version> GetModVersion(Mod mod) {
        if (!TryParseVersion(mod.Version, out var version))
            return Result<Version>.Failure($"Mod {mod.Name} does not have a valid version");

        return Result<Version>.Success(version);;
    }
    
    private static Result<ModManifest> AssertModHasName(ModManifest manifest) {
        if (string.IsNullOrWhiteSpace(manifest.Name))
            return Result<ModManifest>.Failure($"Manifest file for mod {manifest.Name} does not have a name");
        
        return Result<ModManifest>.Success(manifest);
    }

    private static Result<Version> AssertVersionsEqual(Version version, Version expectedVersion, string modName) {
        if (version != expectedVersion)
            return Result<Version>.Failure($"Version in manifest file for mod {modName} does not match the release tag");
        
        return Result<Version>.Success(version);
    }
}