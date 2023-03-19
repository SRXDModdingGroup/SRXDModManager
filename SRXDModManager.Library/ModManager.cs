using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SRXDModManager.Library; 

public class ModManager {
    private string gamePath;
    private string bepInExPath;
    private string pluginsPath;
    
    private GitHubClient gitHubClient;
    private JsonSerializer serializer;
    private SortedDictionary<string, Mod> loadedMods;

    public ModManager(string gamePath) {
        this.gamePath = gamePath;
        bepInExPath = Path.Combine(gamePath, "BepInEx");
        pluginsPath = Path.Combine(bepInExPath, "plugins");

        gitHubClient = new GitHubClient();
        serializer = new JsonSerializer();
        loadedMods = new SortedDictionary<string, Mod>();
    }

    public bool TryGetMod(string name, out Mod mod) => loadedMods.TryGetValue(name, out mod);

    public ActiveBuild GetActiveBuild() {
        if (!File.Exists(Path.Combine(gamePath, "UnityPlayer.dll")))
            return ActiveBuild.Unknown;

        bool monoExists = File.Exists(Path.Combine(gamePath, "UnityPlayer_Mono.dll"));
        bool baseExists = File.Exists(Path.Combine(gamePath, "UnityPlayer_IL2CPP.dll"));

        if (!baseExists && monoExists)
            return ActiveBuild.Base;
        
        if (!monoExists && baseExists)
            return ActiveBuild.Mono;

        return ActiveBuild.Unknown;
    }

    public Result SetActiveBuild(ActiveBuild build) {
        var activeBuild = GetActiveBuild();

        if (activeBuild == ActiveBuild.Unknown)
            return Result.Failure("Current active build is unknown");
        
        if (build == activeBuild)
            return Result.Success();
        
        string activePlayerPath = Path.Combine(gamePath, "UnityPlayer.dll");
        string basePlayerPath = Path.Combine(gamePath, "UnityPlayer_IL2CPP.dll");
        string monoPlayerPath = Path.Combine(gamePath, "UnityPlayer_Mono.dll");

        switch (build) {
            case ActiveBuild.Base:
                File.Move(activePlayerPath, monoPlayerPath);
                File.Move(basePlayerPath, activePlayerPath);
                break;
            case ActiveBuild.Mono:
                File.Move(activePlayerPath, basePlayerPath);
                File.Move(monoPlayerPath, activePlayerPath);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(build), "Must be Base or Mono");
        }
        
        return Result.Success();
    }

    public Result RefreshLoadedMods() {
        loadedMods.Clear();
        
        if (!Directory.Exists(pluginsPath))
            return Result.Failure($"Directory \"{pluginsPath}\" was not found");
        
        foreach (string directory in Directory.GetDirectories(pluginsPath)) {
            string manifestPath = Path.Combine(directory, "manifest.json");
            
            if (!File.Exists(manifestPath))
                continue;

            using var reader = File.OpenText(manifestPath);
            var manifest = JsonConvert.DeserializeObject<Mod>(reader.ReadToEnd());
            
            if (manifest != null)
                loadedMods.Add(manifest.Name, manifest);
        }
        
        return Result.Success();
    }

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

            if (!DeserializeMod(manifestPath)
                    .Then(AssertModHasName)
                    .TryGetValue(out var mod, out message)
                || !GetModVersion(mod)
                    .Then(version => AssertVersionsEqual(version, expectedVersion, mod.Name))
                    .TryGetValue(out _, out message))
                return Result<Mod>.Failure(message);

            string directory = Path.Combine(pluginsPath, mod.Name);

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            foreach (string path in tempFiles)
                File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));

            return Result<Mod>.Success(mod);
        }
        finally {
            foreach (string path in tempFiles) {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    private Result<Mod> DeserializeMod(string path) {
        Mod manifest;
        
        using (var reader = new JsonTextReader(File.OpenText(path)))
            manifest = serializer.Deserialize<Mod>(reader);
        
        if (manifest == null)
            return Result<Mod>.Failure($"Could not deserialize manifest file for mod at {path}");
        
        return Result<Mod>.Success(manifest);
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

    private static Result<Version> GetModVersion(Mod manifest) {
        if (!TryParseVersion(manifest.Version, out var version))
            return Result<Version>.Failure($"Manifest file for mod {manifest.Name} does not have a valid version");

        return Result<Version>.Success(version);;
    }
    
    private static Result<Mod> AssertModHasName(Mod manifest) {
        if (string.IsNullOrWhiteSpace(manifest.Name))
            return Result<Mod>.Failure($"Manifest file for mod {manifest.Name} does not have a name");
        
        return Result<Mod>.Success(manifest);
    }

    private static Result<Version> AssertVersionsEqual(Version version, Version expectedVersion, string modName) {
        if (version != expectedVersion)
            return Result<Version>.Failure($"Version in manifest file for mod {modName} does not match the release tag");
        
        return Result<Version>.Success(version);
    }
}