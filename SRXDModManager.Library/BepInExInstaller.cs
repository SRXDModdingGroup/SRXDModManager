using System.IO;
using System.Threading.Tasks;

namespace SRXDModManager.Library; 

public class BepInExInstaller {
    private GitHubClient gitHubClient = new();

    public Result Uninstall(string gameDirectory, bool full) {
        try {
            Util.DeleteFileIfExists(Path.Combine(gameDirectory, "changelog.txt"));
            Util.DeleteFileIfExists(Path.Combine(gameDirectory, "doorstop_config.ini"));
            Util.DeleteFileIfExists(Path.Combine(gameDirectory, "winhttp.dll"));
            
            string bepInExDirectory = Path.Combine(gameDirectory, "BepInEx");

            if (full) {
                Util.DeleteDirectoryIfExists(bepInExDirectory);

                return Result.Success();
            }
            
            Util.DeleteFileIfExists(Path.Combine(bepInExDirectory, "LogOutput.log"));
            Util.DeleteDirectoryIfExists(Path.Combine(bepInExDirectory, "cache"));
            Util.DeleteDirectoryIfExists(Path.Combine(bepInExDirectory, "core"));
            Util.DeleteDirectoryIfExists(Path.Combine(bepInExDirectory, "patchers"));
            
            return Result.Success();
        }
        catch (IOException e) {
            return Result.Failure(e.Message);
        }
    }

    public Task<Result> Install(string gameDirectory)
        => Util.VerifyDirectoryExists(gameDirectory)
            .Then(_ => gitHubClient.GetLatestRelease(new Address("BepInEx", "BepInEx")))
            .Then(release => Util.GetAsset(release, name => name.StartsWith("BepInEx_x64_")))
            .Then(gitHubClient.DownloadAssetAsStream)
            .Then(stream => PerformDownload(stream, gameDirectory));

    private static async Task<Result> PerformDownload(AssetStream stream, string directory) {
        string parent = Directory.GetParent(directory)?.FullName;
        
        if (string.IsNullOrWhiteSpace(parent))
            return Result.Failure($"{directory} does not have a parent directory");
        
        string tempDirectory = Util.GetUniqueFilePath(parent, stream.Name, ".tmp");

        if (Directory.Exists(tempDirectory))
            Directory.Delete(tempDirectory, true);
            
        Directory.CreateDirectory(tempDirectory);

        try {
            await Util.UnzipStream(stream.Stream, tempDirectory);
            
            string bepInExDirectory = Path.Combine(directory, "BepInEx");

            Directory.CreateDirectory(bepInExDirectory);
            Directory.CreateDirectory(Path.Combine(bepInExDirectory, "cache"));
            Directory.CreateDirectory(Path.Combine(bepInExDirectory, "config"));
            Directory.CreateDirectory(Path.Combine(bepInExDirectory, "core"));
            Directory.CreateDirectory(Path.Combine(bepInExDirectory, "patchers"));
            Directory.CreateDirectory(Path.Combine(bepInExDirectory, "plugins"));

            foreach (string path in Directory.GetFiles(tempDirectory, "*", SearchOption.AllDirectories))
                Util.MoveAndReplace(path, Path.Combine(directory, path.Remove(0, tempDirectory.Length + 1)));

            return Result.Success();
        }
        catch (IOException e) {
            return Result.Failure(e.Message);
        }
        finally {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
    }
}