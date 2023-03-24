using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SRXDModManager.Library; 

internal static class Util {
    public static string GetUniqueFilePath(string directory, string name, string extension) {
        for (int i = 0; i < ushort.MaxValue; i++) {
            string path = Path.Combine(directory, $"{name}_{i}{extension}");

            if (!File.Exists(path))
                return path;
        }

        return string.Empty;
    }
    
    public static Result<string> VerifyDirectoryExists(string directory) {
        directory = directory.Trim();
        
        if (!Directory.Exists(directory))
            return Result<string>.Failure($"Could not find directory {directory}");
        
        return Result<string>.Success(directory);
    }

    public static Result<string> VerifyFileExists(string path) {
        path = path.Trim();
        
        if (!File.Exists(path))
            return Result<string>.Failure($"Could not find file {path}");
        
        return Result<string>.Success(path);
    }
    
    public static Result<Mod> CreateModFromManifest(ModManifest manifest) {
        if (string.IsNullOrWhiteSpace(manifest.Name))
            return Result<Mod>.Failure($"Manifest file for mod {manifest.Name} does not have a name");
        
        if (!Version.TryParse(manifest.Version, out var version))
            return Result<Mod>.Failure($"Manifest file for mod {manifest.Name} does not have a valid version");
        
        if (!Address.TryParse(manifest.Repository, out var address))
            return Result<Mod>.Failure($"Manifest file for mod {manifest.Name} does not have a valid repository");
        
        var manifestDependencies = manifest.Dependencies;
        var dependencies = new List<ModDependency>(manifestDependencies.Length);

        foreach (var manifestDependency in manifestDependencies) {
            if (!CreateModDependencyFromManifestDependency(manifestDependency, manifest.Name)
                    .TryGetValue(out var dependency, out string failureMessage))
                return Result<Mod>.Failure(failureMessage);
            
            dependencies.Add(dependency);
        }

        return Result<Mod>.Success(new Mod(manifest.Name, manifest.Description, version, address, dependencies.ToArray()));
    }

    public static Result<ModDependency> CreateModDependencyFromManifestDependency(ManifestDependency manifestDependency, string modName) {
        if (string.IsNullOrWhiteSpace(manifestDependency.Name))
            return Result<ModDependency>.Failure($"Dependency for mod {modName} does not have a name");
        
        if (!Version.TryParse(manifestDependency.Version, out var version))
            return Result<ModDependency>.Failure($"Dependency for mod {modName} does not have a valid version");
        
        if (!Address.TryParse(manifestDependency.Repository, out var repository))
            return Result<ModDependency>.Failure($"Dependency for mod {modName} does not have a valid repository");
        
        return Result<ModDependency>.Success(new ModDependency(manifestDependency.Name, version, repository));
    }

    public static Result<ModManifest> DeserializeModManifest(string text) {
        ModManifest manifest;

        try {
            manifest = JsonConvert.DeserializeObject<ModManifest>(text);
        }
        catch (JsonException) {
            return Result<ModManifest>.Failure($"Could not deserialize manifest file");
        }

        if (manifest == null)
            return Result<ModManifest>.Failure($"Could not deserialize manifest file");
        
        return Result<ModManifest>.Success(manifest);
    }

    public static Result<GitHubAsset> GetZipAsset(GitHubRelease release) {
        foreach (var asset in release.Assets) {
            if (asset.Name == "plugin.zip")
                return Result<GitHubAsset>.Success(asset);
        }

        return Result<GitHubAsset>.Failure($"Release {release.Name} does not have a plugin.zip file");
    }
    
    public static Result<GitHubAsset> GetManifestAsset(GitHubRelease release) {
        foreach (var asset in release.Assets) {
            if (asset.Name == "manifest.json")
                return Result<GitHubAsset>.Success(asset);
        }

        return Result<GitHubAsset>.Failure($"Release {release.Name} does not have a manifest.json file");
    }

    public static async Task UnzipStream(Stream stream, string directory) {
        using var archive = new ZipArchive(stream);
        
        foreach (var entry in archive.Entries) {
            string path = Path.Combine(directory, entry.FullName);
            string fileDirectory = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(fileDirectory) && !Directory.Exists(fileDirectory))
                Directory.CreateDirectory(fileDirectory);

            using var entryStream = entry.Open();
            using var fileStream = File.OpenWrite(path);

            await entryStream.CopyToAsync(fileStream);
        }
    }

    public static async Task<string> ReadAllTextAsync(string path) {
        using var reader = new StreamReader(File.OpenRead(path));

        return await reader.ReadToEndAsync();
    }
}