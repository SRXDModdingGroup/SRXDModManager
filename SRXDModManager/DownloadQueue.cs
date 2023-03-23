using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using SRXDModManager.Library;

namespace SRXDModManager; 

public class DownloadQueue {
    private ModsClient modsClient;
    private HashSet<string> alreadyQueued;
    private ConcurrentQueue<Task<IReadOnlyList<DownloadRequest>>> taskQueue;
    
    public DownloadQueue(ModsClient modsClient) {
        this.modsClient = modsClient;
        alreadyQueued = new HashSet<string>();
        taskQueue = new ConcurrentQueue<Task<IReadOnlyList<DownloadRequest>>>();
    }

    public void Enqueue(DownloadRequest request) {
        string repository = request.Repository;
        
        if (!IsValidRepository(repository)) {
            Console.WriteLine($"{repository} is not a valid repository");

            return;
        }

        lock (alreadyQueued) {
            if (alreadyQueued.Contains(repository))
                return;

            alreadyQueued.Add(repository);
        }

        taskQueue.Enqueue(PerformDownload(repository, request.ResolveDependencies));
    }

    public void WaitAll() {
        while (taskQueue.Count > 0) {
            if (!taskQueue.TryDequeue(out var task))
                continue;
            
            foreach (var request in task.Result)
                Enqueue(request);
        }
    }
    
    private async Task<IReadOnlyList<DownloadRequest>> PerformDownload(string repository, bool resolveDependencies) {
        if (!(await modsClient.DownloadMod(repository)).TryGetValue(out var mod, out string failureMessage)) {
            Console.WriteLine($"Failed to download mod at {repository}: {failureMessage}");
            
            return Array.Empty<DownloadRequest>();
        }
        
        Console.WriteLine($"Successfully downloaded {mod}");

        if (!resolveDependencies)
            return Array.Empty<DownloadRequest>();
            
        var dependencies = modsClient.GetMissingDependencies(mod);
        var requests = new DownloadRequest[dependencies.Count];

        for (int i = 0; i < dependencies.Count; i++)
            requests[i] = new DownloadRequest(dependencies[i].Repository, true);

        return requests;
    }

    private static bool IsValidRepository(string repository) {
        if (string.IsNullOrWhiteSpace(repository) || repository.Length < 3)
            return false;

        string[] split = repository.Split('/');

        return split.Length == 2 && !string.IsNullOrWhiteSpace(split[0]) && !string.IsNullOrWhiteSpace(split[1]);
    }
}