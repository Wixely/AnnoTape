using System.Text.Json;
using AnnoTape.Core.Models;

namespace AnnoTape.Core.Persistence;

public sealed class CrashSafeStorage(string rootPath)
{
    private readonly string _rootPath = Path.GetFullPath(rootPath);
    private string MediaPath => Path.Combine(_rootPath, "media");
    private string StagingPath => Path.Combine(_rootPath, "staging");
    private string PendingPath => Path.Combine(_rootPath, "pending-operation.json");
    private string HostRestartPath => Path.Combine(_rootPath, "host-restart-project.txt");

    public void Initialize()
    {
        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(MediaPath);
        Directory.CreateDirectory(StagingPath);
    }

    public string CreateStagingPath(string extension)
    {
        Initialize();
        var safeExtension = extension.StartsWith('.') ? extension : $".{extension}";
        return Path.Combine(StagingPath, $"{Guid.NewGuid():N}{safeExtension}");
    }

    public async Task<string> ImportAsync(Stream source, Guid projectId, string extension, CancellationToken cancellationToken = default)
    {
        Initialize();
        var staging = CreateStagingPath(extension);
        await using (var destination = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }

        var projectDirectory = Path.Combine(MediaPath, projectId.ToString("N"));
        Directory.CreateDirectory(projectDirectory);
        var finalPath = Path.Combine(projectDirectory, $"{Guid.NewGuid():N}{Path.GetExtension(staging)}");
        File.Move(staging, finalPath);
        return finalPath;
    }

    public void WritePending(PendingExternalOperation operation) => WriteJsonAtomically(PendingPath, operation);

    public PendingExternalOperation? ReadPending()
    {
        try
        {
            return File.Exists(PendingPath)
                ? JsonSerializer.Deserialize<PendingExternalOperation>(File.ReadAllText(PendingPath))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void ClearPending()
    {
        if (File.Exists(PendingPath)) File.Delete(PendingPath);
    }

    public void WriteHostRestartProject(Guid projectId)
    {
        var temporary = $"{HostRestartPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, projectId.ToString("D"));
        File.Move(temporary, HostRestartPath, true);
    }

    public Guid? TakeHostRestartProject()
    {
        if (!File.Exists(HostRestartPath)) return null;
        try
        {
            return Guid.TryParse(File.ReadAllText(HostRestartPath), out var projectId) ? projectId : null;
        }
        finally
        {
            File.Delete(HostRestartPath);
        }
    }

    public int CleanAbandonedStaging(TimeSpan olderThan)
    {
        Initialize();
        var threshold = DateTime.UtcNow - olderThan;
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(StagingPath))
        {
            if (File.GetLastWriteTimeUtc(path) >= threshold) continue;
            File.Delete(path);
            removed++;
        }
        return removed;
    }

    private static void WriteJsonAtomically<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value));
        File.Move(temporary, path, true);
    }
}

public sealed class DebouncedAutosave(ProjectRepository repository, TimeSpan delay) : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private Task _saveTask = Task.CompletedTask;

    public void Schedule(AnnoProject project)
    {
        lock (_gate)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = new CancellationTokenSource();
            var token = _pending.Token;
            _saveTask = SaveAfterDelayAsync(project, token);
        }
    }

    public async Task FlushAsync(AnnoProject project, CancellationToken cancellationToken = default)
    {
        lock (_gate) _pending?.Cancel();
        await repository.SaveAsync(project, cancellationToken);
    }

    private async Task SaveAfterDelayAsync(AnnoProject project, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await repository.SaveAsync(project, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) _pending?.Cancel();
        try { await _saveTask; } catch (OperationCanceledException) { }
        _pending?.Dispose();
    }
}
