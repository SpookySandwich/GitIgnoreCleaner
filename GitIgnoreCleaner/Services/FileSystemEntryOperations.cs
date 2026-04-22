using System.IO;

namespace GitIgnoreCleaner.Services;

internal static class FileSystemEntryOperations
{
    public static string NormalizePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static bool IsPathWithinRoot(string rootPath, string candidatePath)
    {
        var normalizedRoot = NormalizePath(rootPath);
        var normalizedCandidate = NormalizePath(candidatePath);

        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public static bool PathExists(string fullPath, bool isDirectory)
    {
        return isDirectory ? Directory.Exists(fullPath) : File.Exists(fullPath);
    }

    public static long MeasurePathSize(string fullPath, bool isDirectory, IList<string>? errors = null)
    {
        var normalizedPath = NormalizePath(fullPath);
        return isDirectory
            ? MeasureDirectorySize(normalizedPath, errors)
            : MeasureFileSize(normalizedPath, errors);
    }

    public static void DeletePath(string fullPath, bool isDirectory)
    {
        var normalizedPath = NormalizePath(fullPath);

        if (isDirectory)
        {
            DeleteDirectory(normalizedPath);
            return;
        }

        DeleteFile(normalizedPath);
    }

    public static void TryDeletePath(string fullPath, bool isDirectory)
    {
        try
        {
            DeletePath(fullPath, isDirectory);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static long MeasureDirectorySize(string directoryPath, IList<string>? errors)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        long totalBytes = 0;

        try
        {
            var attributes = File.GetAttributes(directoryPath);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return 0;
            }
        }
        catch (Exception ex)
        {
            errors?.Add(LocalizationService.Format("ErrorInspectEntry", directoryPath, ex.Message));
            return 0;
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(directoryPath);
        }
        catch (Exception ex)
        {
            errors?.Add(LocalizationService.Format("ErrorReadDirectory", directoryPath, ex.Message));
            return 0;
        }

        foreach (var entry in entries)
        {
            try
            {
                var attributes = File.GetAttributes(entry);
                var isDirectory = attributes.HasFlag(FileAttributes.Directory);
                totalBytes += MeasurePathSize(entry, isDirectory, errors);
            }
            catch (Exception ex)
            {
                errors?.Add(LocalizationService.Format("ErrorInspectEntry", entry, ex.Message));
            }
        }

        return totalBytes;
    }

    private static long MeasureFileSize(string filePath, IList<string>? errors)
    {
        if (!File.Exists(filePath))
        {
            return 0;
        }

        try
        {
            return new FileInfo(filePath).Length;
        }
        catch (Exception ex)
        {
            errors?.Add(LocalizationService.Format("ErrorReadFileSize", filePath, ex.Message));
            return 0;
        }
    }

    private static void DeleteDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        var attributes = File.GetAttributes(directoryPath);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            SetWritableAttributes(directoryPath, attributes);
            Directory.Delete(directoryPath, recursive: false);
            return;
        }

        foreach (var childPath in Directory.EnumerateFileSystemEntries(directoryPath))
        {
            var childAttributes = File.GetAttributes(childPath);
            var isDirectory = childAttributes.HasFlag(FileAttributes.Directory);
            DeletePath(childPath, isDirectory);
        }

        SetWritableAttributes(directoryPath, attributes);
        Directory.Delete(directoryPath, recursive: false);
    }

    private static void DeleteFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        var attributes = File.GetAttributes(filePath);
        SetWritableAttributes(filePath, attributes);
        File.Delete(filePath);
    }

    private static void SetWritableAttributes(string fullPath, FileAttributes currentAttributes)
    {
        var writableAttributes =
            currentAttributes &
            ~FileAttributes.ReadOnly &
            ~FileAttributes.Hidden &
            ~FileAttributes.System;

        if (writableAttributes != currentAttributes)
        {
            File.SetAttributes(fullPath, writableAttributes);
        }
    }
}
