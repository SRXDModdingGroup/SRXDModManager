using System.Collections.Generic;
using System.Threading.Tasks;

namespace SRXDModManager; 

public readonly struct DownloadRequest {
    public string Repository { get; }
    
    public bool ResolveDependencies { get; }

    public DownloadRequest(string repository, bool resolveDependencies) {
        Repository = repository;
        ResolveDependencies = resolveDependencies;
    }
}