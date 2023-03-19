﻿using System;
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

    public bool TryGetMod(string name, out Mod mod) {
        lock (loadedMods)
            return loadedMods.TryGetValue(name, out mod);
    }

    public IReadOnlyList<Mod> GetLoadedMods() {
        lock (loadedMods)
            return new List<Mod>(loadedMods.Values);
    }

    public Result SetActiveBuild(ActiveBuild build) {
        if (!GetActiveBuild().TryGetValue(out var activeBuild, out string failureMessage))
            return Result.Failure(failureMessage);
        
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
        catch (IOException e) {
            return Result.Failure(e.Message);
        }
        finally {
            if (File.Exists(tempPlayerPath))
                File.Delete(tempPlayerPath);
        }
        
        return Result.Success();
    }

    public Result<ActiveBuild> GetActiveBuild() {
        if (!VerifyGameDirectoryExists().Then(VerifyUnityPlayerExists).TryGetValue(out string failureMessage))
            return Result<ActiveBuild>.Failure(failureMessage);

        bool monoExists = File.Exists(Path.Combine(gameDirectory, "UnityPlayer_Mono.dll"));
        bool il2CppExists = File.Exists(Path.Combine(gameDirectory, "UnityPlayer_IL2CPP.dll"));

        if (!il2CppExists && monoExists)
            return Result<ActiveBuild>.Success(ActiveBuild.Il2Cpp);
        
        if (!monoExists && il2CppExists)
            return Result<ActiveBuild>.Success(ActiveBuild.Mono);

        return Result<ActiveBuild>.Failure("Active build cannot be determined");
    }

    public Result<IReadOnlyList<Mod>> RefreshLoadedMods() {
        lock (loadedMods) {
            loadedMods.Clear();

            if (!VerifyGameDirectoryExists().Then(VerifyPluginsDirectoryExists).TryGetValue(out string failureMessage))
                return Result<IReadOnlyList<Mod>>.Failure(failureMessage);

            foreach (string directory in Directory.GetDirectories(pluginsDirectory)) {
                string manifestPath = Path.Combine(directory, "manifest.json");

                if (!File.Exists(manifestPath))
                    continue;

                using var reader = File.OpenText(manifestPath);
                var manifest = JsonConvert.DeserializeObject<ModManifest>(reader.ReadToEnd());

                if (manifest == null || !TryParseVersion(manifest.Version, out var version))
                    continue;

                loadedMods[manifest.Name] = CreateModFromManifest(manifest, directory, version);
            }

            return Result<IReadOnlyList<Mod>>.Success(new List<Mod>(loadedMods.Values));
        }
    }

    public async Task<Result<Version>> GetLatestVersion(Mod mod) {
        if (!(await gitHubClient.GetLatestRelease(mod.Repository))
                .Then(GetReleaseVersion)
                .TryGetValue(out var latestVersion, out string failureMessage))
            return Result<Version>.Failure(failureMessage);

        return Result<Version>.Success(latestVersion);
    }

    public async Task<Result<Mod>> DownloadMod(string repository) {
        if (!(await gitHubClient.GetLatestRelease(repository))
                .TryGetValue(out var release, out string failureMessage)
            || !GetReleaseVersion(release)
                .TryGetValue(out var expectedVersion, out failureMessage)
            || !GetZipAsset(release)
                .TryGetValue(out var zipAsset, out failureMessage)
            || !(await PerformDownload(zipAsset, expectedVersion, repository))
                .TryGetValue(out var mod, out failureMessage))
            return Result<Mod>.Failure(failureMessage);
        
        lock (loadedMods)
            loadedMods[mod.Name] = mod;

        return Result<Mod>.Success(mod);
    }

    private Result VerifyGameDirectoryExists() {
        if (!Directory.Exists(gameDirectory))
            return Result.Failure($"Could not find game directory {gameDirectory}");
        
        return Result.Success();
    }
    
    private Result VerifyPluginsDirectoryExists() {
        if (!Directory.Exists(pluginsDirectory))
            return Result.Failure($"Could not find plugins directory {pluginsDirectory}. Ensure that BepInEx is installed");
        
        return Result.Success();
    }

    private Result VerifyUnityPlayerExists() {
        if (!File.Exists(Path.Combine(gameDirectory, "UnityPlayer.dll")))
            return Result.Failure("Could not find UnityPlayer.dll");
        
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
            .TryGetValue(out var stream, out string failureMessage))
            return Result<Mod>.Failure(failureMessage);

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
                    .Then(ValidateManifestProperties)
                    .TryGetValue(out var manifest, out failureMessage)
                || !GetModVersion(manifest)
                    .Then(version => AssertVersionsEqual(version, expectedVersion, manifest.Name))
                    .TryGetValue(out var version, out failureMessage))
                return Result<Mod>.Failure(failureMessage);

            string directory = Path.Combine(pluginsDirectory, manifest.Name);

            if (Directory.Exists(directory))
                Directory.Delete(directory, true);

            Directory.CreateDirectory(directory);

            foreach (string path in tempFiles)
                File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));

            return Result<Mod>.Success(CreateModFromManifest(manifest, directory, version));
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

    private static Mod CreateModFromManifest(ModManifest manifest, string directory, Version version) {
        var manifestDependencies = manifest.Dependencies;
        var dependencies = new List<ModDependency>(manifestDependencies.Length);

        for (int i = 0; i < dependencies.Count; i++) {
            var manifestDependency = manifestDependencies[i];

            if (TryParseVersion(manifestDependency.Version, out var dependencyVersion))
                dependencies.Add(new ModDependency(manifestDependency.Name, dependencyVersion, manifest.Repository));
        }

        return new Mod(directory, manifest.Name, manifest.Description, version, manifest.Repository, dependencies.ToArray());
    }
    
    private static Result<GitHubAsset> GetZipAsset(GitHubRelease release) {
        foreach (var asset in release.Assets) {
            if (asset.Name == "plugin.zip")
                return Result<GitHubAsset>.Success(asset);
        }

        return Result<GitHubAsset>.Failure($"Release {release.Name} does not have a plugin.zip file");
    }

    private static Result<ModManifest> ValidateManifestProperties(ModManifest manifest) {
        if (string.IsNullOrWhiteSpace(manifest.Name))
            return Result<ModManifest>.Failure($"Manifest file for mod {manifest.Name} does not have a name");
        
        if (string.IsNullOrWhiteSpace(manifest.Version))
            return Result<ModManifest>.Failure($"Manifest file for mod {manifest.Name} does not have a version");
        
        if (string.IsNullOrWhiteSpace(manifest.Repository))
            return Result<ModManifest>.Failure($"Manifest file for mod {manifest.Name} does not have a repository");
        
        return Result<ModManifest>.Success(manifest);
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

    private static Result<Version> AssertVersionsEqual(Version version, Version expectedVersion, string modName) {
        if (version != expectedVersion)
            return Result<Version>.Failure($"Version in manifest file for mod {modName} does not match the release tag");
        
        return Result<Version>.Success(version);
    }
}