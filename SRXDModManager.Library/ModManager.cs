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
    private SortedDictionary<string, Mod> loadedMods;

    public ModManager(string modsPath) {
        this.modsPath = modsPath;

        gitHubClient = new GitHubClient();
        loadedMods = new SortedDictionary<string, Mod>();
    }

    public bool TryGetMod(string name, out Mod mod) => loadedMods.TryGetValue(name, out mod);

    public async Task DownloadMod(string repository) {
        var release = await gitHubClient.GetLatestRelease(repository);

        if (!TryParseVersion(release.TagName, out _))
            throw new Exception($"Tag for the latest release of mod at {repository} is not a valid version string");
        
        Asset zipAsset = null;

        foreach (var asset in release.Assets) {
            if (asset.Name != "plugin.zip")
                continue;

            zipAsset = asset;

            break;
        }

        if (zipAsset == null)
            throw new Exception($"Latest release of mod at {repository} does not have a plugin.zip file");

        var files = new List<string>();

        try {
            using var stream = await gitHubClient.DownloadAsset(zipAsset);
            using var archive = new ZipArchive(stream);

            foreach (var entry in archive.Entries) {
                string path = Path.Combine(Path.GetTempPath(), entry.Name);
                
                files.Add(path);

                using var entryStream = entry.Open();
                using var fileStream = File.OpenWrite(path);

                await entryStream.CopyToAsync(fileStream);
            }

            string manifestPath = Path.Combine(Path.GetTempPath(), "manifest.json");

            if (!files.Contains(manifestPath))
                throw new Exception($"Mod at {repository} does not have manifest.json file");

            string name;
            Mod manifest;

            using (var reader = File.OpenText(manifestPath)) {
                manifest = JsonConvert.DeserializeObject<Mod>(await reader.ReadToEndAsync());

                if (manifest == null)
                    throw new JsonException($"Could not deserialize manifest file for mod at {repository}");

                if (string.IsNullOrWhiteSpace(manifest.Name))
                    throw new Exception($"Manifest file for mod {manifest.Name} does not have a name");
                
                if (!TryParseVersion(manifest.Version, out _))
                    throw new Exception($"Manifest file for mod {manifest.Name} does not have a valid version");
                
                name = manifest.Name;
            }

            string directory = Path.Combine(modsPath, name);

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            foreach (string path in files)
                File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));

            loadedMods.Add(manifest.Name, manifest);
        }
        finally {
            foreach (string path in files) {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

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
        if (!TryParseVersion(mod.Version, out var currentVersion))
            throw new Exception($"Manifest file for mod {mod.Name} does not have a valid version");
        
        string repository = mod.Repository;
        var release = await gitHubClient.GetLatestRelease(repository);

        if (!TryParseVersion(release.TagName, out var latestVersion))
            throw new Exception($"Tag for the latest release of mod {repository} is not a valid version string");

        return latestVersion > currentVersion;
    }

    private static bool TryParseVersion(string text, out Version version) {
        for (int i = text.Length - 1; i >= 0; i--) {
            char character = text[i];

            if (!char.IsDigit(character) && character != '.')
                return Version.TryParse(text.Substring(i + 1), out version);
        }

        return Version.TryParse(text, out version);
    }
}