using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SRXDModManager.Library; 

public class ModsClient {
    private GitHubClient gitHubClient = new();
    private int tempCounter;

    public Task<Result<Mod>> DownloadMod(Repository repository, string directory)
        => Util.VerifyDirectoryExists(directory)
            .Then(_ => gitHubClient.GetLatestRelease(repository))
            .Then(Util.GetZipAsset)
            .Then(gitHubClient.DownloadAssetFromStream)
            .Then(stream => PerformDownload(stream, directory));

    public Task<Result<Mod>> GetLatestModInfo(Repository repository)
        => gitHubClient.GetLatestRelease(repository)
            .Then(Util.GetManifestAsset)
            .Then(gitHubClient.DownloadAssetAsString)
            .Then(Util.DeserializeModManifest)
            .Then(Util.CreateModFromManifest);
    
    private async Task<Result<Mod>> PerformDownload(AssetStream stream, string directory) {
        string tempDirectory = Path.Combine(directory, $"{stream.Name}_{Interlocked.Increment(ref tempCounter)}.tmp");

        if (Directory.Exists(tempDirectory))
            Directory.Delete(tempDirectory, true);
            
        Directory.CreateDirectory(tempDirectory);

        try {
            await Util.UnzipStream(stream.Stream, tempDirectory);

            string manifestPath = Path.Combine(tempDirectory, "manifest.json");

            if (!File.Exists(manifestPath))
                return Result<Mod>.Failure($"Mod {stream.Name} does not have a manifest.json file");

            if (!Util.DeserializeModManifest(await Util.ReadAllTextAsync(manifestPath))
                    .Then(Util.CreateModFromManifest)
                    .TryGetValue(out var mod, out string failureMessage))
                return Result<Mod>.Failure(failureMessage);

            string modDirectory = Path.Combine(directory, mod.Name);

            if (Directory.Exists(modDirectory))
                Directory.Delete(modDirectory, true);

            Directory.Move(tempDirectory, modDirectory);

            return Result<Mod>.Success(mod);
        }
        catch (IOException e) {
            return Result<Mod>.Failure(e.Message);
        }
        finally {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
    }
}