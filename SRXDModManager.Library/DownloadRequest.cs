using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace SRXDModManager.Library;

internal class DownloadRequest {
    private static int tempCounter;

    private Repository repository;
    private string pluginsDirectory;
    private GitHubClient gitHubClient;
    private TaskCompletionSource<Result<Mod>> completionSource;

    public DownloadRequest(Repository repository, string pluginsDirectory, GitHubClient gitHubClient) {
        this.repository = repository;
        this.pluginsDirectory = pluginsDirectory;
        this.gitHubClient = gitHubClient;
        completionSource = new TaskCompletionSource<Result<Mod>>();
    }

    public async Task<Result<Mod>> Execute(CancellationToken ct) {
        Result<Mod> result;
        
        try {
            result = await DoExecute(ct);
        }
        catch (Exception e) {
            completionSource.SetException(e);

            throw;
        }
        
        if (ct.IsCancellationRequested)
            completionSource.SetCanceled();
        else
            completionSource.SetResult(result);

        return result;
    }

    public async Task<Result<Mod>> GetResult() => await completionSource.Task;

    private async Task<Result<Mod>> DoExecute(CancellationToken ct) {
        if (!(await gitHubClient.GetLatestRelease(repository))
                .Then(Util.GetZipAsset)
                .TryGetValue(out var zipAsset, out string failureMessage)
            || !(await gitHubClient.DownloadAssetFromStream(zipAsset))
                .TryGetValue(out var stream, out failureMessage)
            || !(await PerformDownload(stream, zipAsset.Name)).TryGetValue(out var mod, out failureMessage))
            return Result<Mod>.Failure(failureMessage);

        return Result<Mod>.Success(mod);
    }
    
    private async Task<Result<Mod>> PerformDownload(Stream stream, string name) {
        string tempDirectory = Path.Combine(pluginsDirectory, $"{name}_{Interlocked.Increment(ref tempCounter)}.tmp");

        if (Directory.Exists(tempDirectory))
            Directory.Delete(tempDirectory, true);
            
        Directory.CreateDirectory(tempDirectory);

        try {
            await Util.UnzipStream(stream, tempDirectory);

            string manifestPath = Path.Combine(tempDirectory, "manifest.json");

            if (!File.Exists(manifestPath))
                return Result<Mod>.Failure($"Mod at {repository} does not have a manifest.json file");

            if (!Util.DeserializeModManifest(await Util.ReadAllTextAsync(manifestPath))
                    .Then(Util.CreateModFromManifest)
                    .TryGetValue(out var mod, out string failureMessage))
                return Result<Mod>.Failure(failureMessage);

            string directory = Path.Combine(pluginsDirectory, mod.Name);

            if (Directory.Exists(directory))
                Directory.Delete(directory, true);

            Directory.Move(tempDirectory, directory);

            return Result<Mod>.Success(mod);
        }
        catch (IOException) {
            return Result<Mod>.Failure("An IO exception occurred");
        }
        finally {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
    }
}