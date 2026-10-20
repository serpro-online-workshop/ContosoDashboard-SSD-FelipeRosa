namespace ContosoDashboard.Services;

public sealed class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";

    public string RootPath { get; set; } = "AppData/uploads";
    public int MaxFilesPerBatch { get; set; } = 10;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RootPath) || Path.IsPathRooted(RootPath))
        {
            throw new InvalidOperationException("Document storage root must be a non-empty relative path.");
        }

        if (MaxFilesPerBatch is < 1 or > 100)
        {
            throw new InvalidOperationException("Maximum files per batch must be between 1 and 100.");
        }
    }

    public string ResolveRoot(string contentRootPath)
    {
        Validate();
        return Path.GetFullPath(Path.Combine(contentRootPath, RootPath));
    }
}

public sealed class MalwareScannerOptions
{
    public const string SectionName = "MalwareScanner";

    public string ExecutablePath { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;

    public void Validate()
    {
        if (TimeoutSeconds is < 1 or > 600)
        {
            throw new InvalidOperationException("Malware scanner timeout must be between 1 and 600 seconds.");
        }
    }
}
