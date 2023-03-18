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
    private SortedDictionary<string, ModManifest> loadedMods;

    public ModManager(string modsPath) {
        this.modsPath = modsPath;

        gitHubClient = new GitHubClient();
        loadedMods = new SortedDictionary<string, ModManifest>();
    }

    public async Task DownloadMod(string repository) {
        var release = await gitHubClient.GetLatestRelease(repository);
        string zipUrl = null;

        foreach (var asset in release.Assets) {
            if (asset.Name != "plugin.zip")
                continue;

            zipUrl = asset.Url;

            break;
        }

        if (zipUrl == null)
            throw new Exception($"Mod {repository} does not have plugin.zip file");

        var files = new List<string>();

        try {
            using var stream = await gitHubClient.DownloadFile(zipUrl);
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
                throw new Exception($"Mod {repository} does not have manifest.json file");

            string name;
            ModManifest manifest;

            using (var reader = File.OpenText(manifestPath)) {
                manifest = JsonConvert.DeserializeObject<ModManifest>(await reader.ReadToEndAsync());

                if (manifest == null)
                    throw new JsonException($"Could not deserialize manifest file for mod {repository}");

                if (string.IsNullOrWhiteSpace(manifest.Name))
                    throw new Exception($"Manifest file for mod {repository} does not have a name");

                name = manifest.Name;
            }

            string directory = Path.Combine(modsPath, name);

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            foreach (string path in files)
                File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));

            loadedMods.Add(manifest.Repository, manifest);
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
            var manifest = JsonConvert.DeserializeObject<ModManifest>(await reader.ReadToEndAsync());
            
            if (manifest != null)
                loadedMods.Add(manifest.Repository, manifest);
        }
    }
}