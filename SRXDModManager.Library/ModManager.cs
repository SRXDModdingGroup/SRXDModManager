using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SRXDModManager.Library; 

public class ModManager {
    private string modsPath;
    
    private GitHubClient gitHubClient;
    private JsonSerializer serializer;
    private SortedDictionary<string, Mod> loadedMods;

    public ModManager(string modsPath) {
        this.modsPath = modsPath;

        gitHubClient = new GitHubClient();
        serializer = new JsonSerializer();
        loadedMods = new SortedDictionary<string, Mod>();
    }

    public bool TryGetMod(string name, out Mod mod) => loadedMods.TryGetValue(name, out mod);

    public async Task RefreshLoadedMods() {
        loadedMods.Clear();
        
        if (!Directory.Exists(modsPath))
            throw new DirectoryNotFoundException($"Directory \"{modsPath}\" was not found");
        
        foreach (string directory in Directory.GetDirectories(modsPath)) {
            string manifestPath = Path.Combine(directory, "manifest.json");
            
            if (!File.Exists(manifestPath))
                continue;

            using var reader = File.OpenText(manifestPath);
            var manifest = JsonConvert.DeserializeObject<Mod>(await reader.ReadToEndAsync());
            
            if (manifest != null)
                loadedMods.Add(manifest.Name, manifest);
        }
    }

    public async Task<bool> NeedsUpdate(Mod mod) {
        var currentVersion = GetManifestVersion(mod);
        var latestVersion = GetReleaseVersion(mod.Repository, await gitHubClient.GetLatestRelease(mod.Repository));

        return latestVersion > currentVersion;
    }

    public async Task<Mod> DownloadMod(string repository) {
        var release = await gitHubClient.GetLatestRelease(repository);
        var expectedVersion = GetReleaseVersion(repository, release);
        Asset zipAsset = null;

        foreach (var asset in release.Assets) {
            if (asset.Name != "plugin.zip")
                continue;

            zipAsset = asset;

            break;
        }

        if (zipAsset == null)
            throw new Exception($"Latest release of mod at {repository} does not have a plugin.zip file");

        var mod = await PerformDownload(zipAsset, expectedVersion, repository);

        loadedMods[mod.Name] = mod;

        return mod;
    }

    private async Task<Mod> PerformDownload(Asset zipAsset, Version expectedVersion, string repository) {
        var tempFiles = new List<string>();

        try {
            using (var archive = new ZipArchive(await gitHubClient.DownloadAsset(zipAsset))) {
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
                throw new Exception($"Mod at {repository} does not have manifest.json file");

            Mod manifest;

            using (var reader = new JsonTextReader(File.OpenText(manifestPath)))
                manifest = serializer.Deserialize<Mod>(reader);

            if (manifest == null)
                throw new JsonException($"Could not deserialize manifest file for mod at {repository}");
            
            ValidateManifest(manifest, expectedVersion);
            
            string directory = Path.Combine(modsPath, manifest.Name);

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            foreach (string path in tempFiles)
                File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));

            return manifest;
        }
        finally {
            foreach (string path in tempFiles) {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    private static void ValidateManifest(Mod manifest, Version expectedVersion) {
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new Exception($"Manifest file for mod {manifest.Name} does not have a name");

        var version = GetManifestVersion(manifest);

        if (version != expectedVersion)
            throw new Exception($"Version in manifest file for mod {manifest.Name} does not match the release tag");
    }

    private static bool TryParseVersion(string text, out Version version) {
        for (int i = text.Length - 1; i >= 0; i--) {
            char character = text[i];

            if (!char.IsDigit(character) && character != '.')
                return Version.TryParse(text.Substring(i + 1), out version);
        }

        return Version.TryParse(text, out version);
    }

    private static Version GetReleaseVersion(string repository, GitHubRelease release) {
        if (!TryParseVersion(release.TagName, out var version))
            throw new Exception($"Tag for the latest release of mod {repository} is not a valid version string");

        return version;
    }

    private static Version GetManifestVersion(Mod manifest) {
        if (!TryParseVersion(manifest.Version, out var version))
            throw new Exception($"Manifest file for mod {manifest.Name} does not have a valid version");

        return version;
    }
}