using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GitIgnoreCleaner.Services;

public enum ReversibleDeleteEntryState
{
    PendingDelete,
    InTrash,
    PendingRestore
}

public sealed class ReversibleDeleteSession
{
    public int SchemaVersion { get; set; } = 2;
    public string SessionId { get; set; } = string.Empty;
    public string ScanRootPath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string PayloadRootPath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<ReversibleDeleteEntry> Entries { get; set; } = [];
}

public sealed class ReversibleDeleteEntry
{
    public string OriginalPath { get; set; } = string.Empty;
    public string PayloadPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long SizeBytes { get; set; }
    public ReversibleDeleteEntryState State { get; set; } = ReversibleDeleteEntryState.InTrash;
}

public sealed class ReversibleTrashService
{
    public const string ReservedFolderName = ".gitignorecleaner-trash";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public DeleteResult MoveToTrash(
        string scanRootPath,
        IReadOnlyList<DeletionPlanEntry> targets,
        IProgress<DeletionPlanEntry>? progress = null)
    {
        var result = new DeleteResult();
        var normalizedRootPath = FileSystemEntryOperations.NormalizePath(scanRootPath);
        var session = CreateSession(normalizedRootPath);

        foreach (var target in targets
                     .OrderByDescending(item => item.FullPath.Length)
                     .ThenBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedTargetPath = FileSystemEntryOperations.NormalizePath(target.FullPath);

            try
            {
                if (!FileSystemEntryOperations.IsPathWithinRoot(normalizedRootPath, normalizedTargetPath))
                {
                    throw new InvalidOperationException("The selected item is outside the active scan root.");
                }

                var relativePath = Path.GetRelativePath(normalizedRootPath, normalizedTargetPath);
                if (relativePath is "." or "")
                {
                    throw new InvalidOperationException("The scan root itself can't be moved to reversible trash.");
                }

                var payloadPath = Path.Combine(session.PayloadRootPath, relativePath);
                var entry = new ReversibleDeleteEntry
                {
                    OriginalPath = normalizedTargetPath,
                    PayloadPath = payloadPath,
                    IsDirectory = target.IsDirectory,
                    SizeBytes = target.SizeBytes,
                    State = ReversibleDeleteEntryState.PendingDelete
                };

                session.Entries.Add(entry);
                SaveSession(session);

                try
                {
                    FileSystemEntryOperations.MovePath(normalizedTargetPath, payloadPath, target.IsDirectory);
                }
                catch
                {
                    session.Entries.Remove(entry);
                    TrySaveOrDeleteSession(session);
                    FileSystemEntryOperations.TryDeletePath(payloadPath, target.IsDirectory);
                    throw;
                }

                entry.State = ReversibleDeleteEntryState.InTrash;
                try
                {
                    SaveSession(session);
                }
                catch (Exception ex)
                {
                    result.Errors.Add(
                        $"Moved {normalizedTargetPath} into reversible trash, but GitIgnoreCleaner couldn't finish updating the restore journal. It will reconcile that automatically next time it loads restore data. {ex.Message}");
                }

                result.DeletedEntries.Add(target);
                progress?.Report(target);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to move {normalizedTargetPath} to reversible trash: {ex.Message}");
            }
        }

        if (session.Entries.Count == 0)
        {
            DeleteSessionArtifacts(session);
            return result;
        }

        result.Session = session;
        return result;
    }

    public RestoreResult Restore(ReversibleDeleteSession session)
    {
        var result = new RestoreResult();
        NormalizeLoadedSession(session);

        var entries = session.Entries
            .OrderBy(entry => entry.OriginalPath.Length)
            .ThenBy(entry => entry.OriginalPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var entry in entries)
        {
            try
            {
                if (!FileSystemEntryOperations.PathExists(entry.PayloadPath, entry.IsDirectory))
                {
                    session.Entries.Remove(entry);
                    TrySaveOrDeleteSession(session);
                    continue;
                }

                if (FileSystemEntryOperations.PathExists(entry.OriginalPath, entry.IsDirectory))
                {
                    result.Errors.Add($"Can't restore {entry.OriginalPath} because something already exists there.");
                    continue;
                }

                entry.State = ReversibleDeleteEntryState.PendingRestore;
                SaveSession(session);

                try
                {
                    FileSystemEntryOperations.MovePath(entry.PayloadPath, entry.OriginalPath, entry.IsDirectory);
                }
                catch
                {
                    entry.State = ReversibleDeleteEntryState.InTrash;
                    TrySaveSession(session);
                    throw;
                }

                session.Entries.Remove(entry);
                try
                {
                    SaveOrDeleteSession(session);
                }
                catch (Exception ex)
                {
                    result.Errors.Add(
                        $"Restored {entry.OriginalPath}, but GitIgnoreCleaner couldn't finish cleaning up the restore journal. It will reconcile that automatically next time it loads restore data. {ex.Message}");
                }

                result.RestoredCount++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to restore {entry.OriginalPath}: {ex.Message}");
            }
        }

        NormalizeLoadedSession(session);
        result.RemainingSession = session.Entries.Count > 0 ? session : null;
        return result;
    }

    public ReversibleDeleteSession? TryLoadLatestSession(string scanRootPath)
    {
        if (string.IsNullOrWhiteSpace(scanRootPath) || !Directory.Exists(scanRootPath))
        {
            return null;
        }

        var normalizedRootPath = FileSystemEntryOperations.NormalizePath(scanRootPath);
        var manifestDirectory = GetManifestDirectory(normalizedRootPath);
        if (!Directory.Exists(manifestDirectory))
        {
            return null;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(manifestDirectory, "*.json").OrderByDescending(Path.GetFileName))
        {
            try
            {
                var session = LoadSession(manifestPath);
                if (session == null)
                {
                    continue;
                }

                if (!string.Equals(
                        FileSystemEntryOperations.NormalizePath(session.ScanRootPath),
                        normalizedRootPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                NormalizeLoadedSession(session);
                if (session.Entries.Count == 0)
                {
                    DeleteSessionArtifacts(session);
                    continue;
                }

                TrySaveSession(session);
                return session;
            }
            catch
            {
                // Ignore invalid manifests and keep looking for the next valid session.
            }
        }

        return null;
    }

    private static ReversibleDeleteSession CreateSession(string normalizedRootPath)
    {
        var sessionId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        var manifestDirectory = GetManifestDirectory(normalizedRootPath);
        var payloadRoot = Path.Combine(normalizedRootPath, ReservedFolderName, sessionId, "payload");

        Directory.CreateDirectory(payloadRoot);
        TryMarkHidden(Path.Combine(normalizedRootPath, ReservedFolderName));
        Directory.CreateDirectory(manifestDirectory);

        var session = new ReversibleDeleteSession
        {
            SessionId = sessionId,
            ScanRootPath = normalizedRootPath,
            ManifestPath = Path.Combine(manifestDirectory, $"{sessionId}.json"),
            PayloadRootPath = FileSystemEntryOperations.NormalizePath(payloadRoot),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        SaveSession(session);
        return session;
    }

    private static ReversibleDeleteSession? LoadSession(string manifestPath)
    {
        using var stream = File.OpenRead(manifestPath);
        return JsonSerializer.Deserialize<ReversibleDeleteSession>(stream);
    }

    private static void NormalizeLoadedSession(ReversibleDeleteSession session)
    {
        session.ScanRootPath = FileSystemEntryOperations.NormalizePath(session.ScanRootPath);
        session.PayloadRootPath = FileSystemEntryOperations.NormalizePath(session.PayloadRootPath);

        var changed = false;

        foreach (var entry in session.Entries.ToList())
        {
            var normalizedOriginalPath = FileSystemEntryOperations.NormalizePath(entry.OriginalPath);
            var normalizedPayloadPath = FileSystemEntryOperations.NormalizePath(entry.PayloadPath);

            if (!string.Equals(entry.OriginalPath, normalizedOriginalPath, StringComparison.Ordinal))
            {
                entry.OriginalPath = normalizedOriginalPath;
                changed = true;
            }

            if (!string.Equals(entry.PayloadPath, normalizedPayloadPath, StringComparison.Ordinal))
            {
                entry.PayloadPath = normalizedPayloadPath;
                changed = true;
            }

            if (FileSystemEntryOperations.PathExists(entry.PayloadPath, entry.IsDirectory))
            {
                if (entry.State != ReversibleDeleteEntryState.InTrash)
                {
                    entry.State = ReversibleDeleteEntryState.InTrash;
                    changed = true;
                }

                continue;
            }

            session.Entries.Remove(entry);
            changed = true;
        }

        session.Entries.Sort(CompareEntries);

        if (changed)
        {
            TrySaveOrDeleteSession(session);
        }
    }

    private static void SaveOrDeleteSession(ReversibleDeleteSession session)
    {
        if (session.Entries.Count == 0)
        {
            DeleteSessionArtifacts(session);
            return;
        }

        SaveSession(session);
    }

    private static void TrySaveOrDeleteSession(ReversibleDeleteSession session)
    {
        try
        {
            SaveOrDeleteSession(session);
        }
        catch
        {
            // Best-effort reconciliation only.
        }
    }

    private static void SaveSession(ReversibleDeleteSession session)
    {
        var manifestDirectory = Path.GetDirectoryName(session.ManifestPath);
        if (!string.IsNullOrWhiteSpace(manifestDirectory))
        {
            Directory.CreateDirectory(manifestDirectory);
        }

        var tempPath = session.ManifestPath + ".tmp";
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, session, JsonOptions);
            }

            File.Move(tempPath, session.ManifestPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }

            throw;
        }
    }

    private static void TrySaveSession(ReversibleDeleteSession session)
    {
        try
        {
            SaveSession(session);
        }
        catch
        {
            // Best-effort reconciliation only.
        }
    }

    private static string GetManifestDirectory(string normalizedRootPath)
    {
        var rootHash = ComputeRootHash(normalizedRootPath);
        return Path.Combine(GetAppDataRoot(), "sessions", rootHash);
    }

    private static string GetAppDataRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GitIgnoreCleaner",
            "trash");
    }

    private static string ComputeRootHash(string normalizedRootPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRootPath));
        return Convert.ToHexString(hash[..12]);
    }

    private static void DeleteSessionArtifacts(ReversibleDeleteSession session)
    {
        FileSystemEntryOperations.TryDeletePath(session.PayloadRootPath, isDirectory: true);

        try
        {
            if (File.Exists(session.ManifestPath))
            {
                File.Delete(session.ManifestPath);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }

        var payloadBoundary = Path.Combine(session.ScanRootPath, ReservedFolderName);
        TryPruneEmptyParents(Path.GetDirectoryName(session.PayloadRootPath), payloadBoundary);
        TryPruneEmptyParents(Path.GetDirectoryName(session.ManifestPath), GetAppDataRoot());
    }

    private static void TryPruneEmptyParents(string? startPath, string? stopAtPath)
    {
        if (string.IsNullOrWhiteSpace(startPath) || string.IsNullOrWhiteSpace(stopAtPath))
        {
            return;
        }

        var currentPath = FileSystemEntryOperations.NormalizePath(startPath);
        var boundaryPath = FileSystemEntryOperations.NormalizePath(stopAtPath);

        while (FileSystemEntryOperations.IsPathWithinRoot(boundaryPath, currentPath) &&
               !string.Equals(currentPath, boundaryPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(currentPath) || Directory.EnumerateFileSystemEntries(currentPath).Any())
                {
                    return;
                }

                Directory.Delete(currentPath, recursive: false);
            }
            catch
            {
                return;
            }

            var parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(parentPath))
            {
                return;
            }

            currentPath = FileSystemEntryOperations.NormalizePath(parentPath);
        }
    }

    private static void TryMarkHidden(string folderPath)
    {
        try
        {
            Directory.CreateDirectory(folderPath);
            var attributes = File.GetAttributes(folderPath);
            if (!attributes.HasFlag(FileAttributes.Hidden))
            {
                File.SetAttributes(folderPath, attributes | FileAttributes.Hidden);
            }
        }
        catch
        {
            // Hidden is only a nicety; ignore failures.
        }
    }

    private static int CompareEntries(ReversibleDeleteEntry left, ReversibleDeleteEntry right)
    {
        var lengthComparison = left.OriginalPath.Length.CompareTo(right.OriginalPath.Length);
        if (lengthComparison != 0)
        {
            return lengthComparison;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left.OriginalPath, right.OriginalPath);
    }
}
