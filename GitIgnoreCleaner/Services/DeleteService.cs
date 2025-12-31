using System.Runtime.InteropServices;
using GitIgnoreCleaner.Models;

namespace GitIgnoreCleaner.Services;

public sealed class DeleteResult
{
    public List<string> Errors { get; } = [];
    public List<ScanNode> DeletedNodes { get; } = [];
}

public sealed class DeleteService
{
    public Task<DeleteResult> DeleteAsync(IEnumerable<ScanNode> nodes, bool permanentlyDelete, IProgress<ScanNode>? progress = null)
    {
        return Task.Run(() =>
        {
            var targets = GetDeletionTargets(nodes);
            return DeleteTargets(targets, permanentlyDelete, progress);
        });
    }

    public Task<DeleteResult> DeleteTargetsAsync(List<ScanNode> targets, bool permanentlyDelete, IProgress<ScanNode>? progress = null)
    {
        return Task.Run(() => DeleteTargets(targets, permanentlyDelete, progress));
    }

    public List<ScanNode> GetDeletionTargets(IEnumerable<ScanNode> nodes)
    {
        var targets = new List<ScanNode>();
        foreach (var node in nodes)
        {
            CollectTargets(node, targets);
        }
        return targets;
    }

    private void CollectTargets(ScanNode node, List<ScanNode> targets)
    {
        if (node.IsChecked == false)
        {
            return;
        }

        if (node.IsCandidate && node.IsChecked == true)
        {
            targets.Add(node);
            return;
        }

        foreach (var child in node.Children)
        {
            CollectTargets(child, targets);
        }
    }

    private DeleteResult DeleteTargets(IReadOnlyList<ScanNode> targets, bool permanentlyDelete, IProgress<ScanNode>? progress)
    {
        if (permanentlyDelete)
        {
            return DeletePermanently(targets, progress);
        }
        else
        {
            return DeleteToRecycleBin(targets, progress);
        }
    }

    private DeleteResult DeletePermanently(IReadOnlyList<ScanNode> targets, IProgress<ScanNode>? progress)
    {
        var result = new DeleteResult();
        foreach (var target in targets
                     .OrderBy(item => item.IsDirectory)
                     .ThenByDescending(item => item.FullPath.Length))
        {
            try
            {
                DeletePermanentlyNode(target);
                result.DeletedNodes.Add(target);
                progress?.Report(target);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to delete {target.FullPath}: {ex.Message}");
            }
        }
        return result;
    }

    private void DeletePermanentlyNode(ScanNode target)
    {
        if (target.IsDirectory)
        {
            if (Directory.Exists(target.FullPath))
            {
                var recursive = true;
                try
                {
                    var attributes = File.GetAttributes(target.FullPath);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        recursive = false;
                    }
                }
                catch
                {
                    // Best-effort attribute check; fall back to recursive delete.
                }

                Directory.Delete(target.FullPath, recursive);
            }
        }
        else if (File.Exists(target.FullPath))
        {
            File.SetAttributes(target.FullPath, FileAttributes.Normal);
            File.Delete(target.FullPath);
        }
    }

    private DeleteResult DeleteToRecycleBin(IReadOnlyList<ScanNode> targets, IProgress<ScanNode>? progress)
    {
        var result = new DeleteResult();
        // Batch paths to avoid calling SHFileOperation too many times.
        // Windows limit is roughly 32k characters for the buffer in some versions.
        const int MaxBatchSize = 50; 
        
        var nodes = targets.ToList();
        
        for (int i = 0; i < nodes.Count; i += MaxBatchSize)
        {
            var batch = nodes.Skip(i).Take(MaxBatchSize).ToList();
            var paths = batch.Select(t => t.FullPath).ToList();
            try
            {
                RecyclePaths(paths);
                result.DeletedNodes.AddRange(batch);
                foreach (var item in batch)
                {
                    progress?.Report(item);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to recycle batch starting with {batch[0].FullPath}: {ex.Message}");
            }
        }
        return result;
    }

    private void RecyclePaths(List<string> paths)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var path in paths)
        {
            sb.Append(path);
            sb.Append('\0');
        }
        sb.Append('\0');

        var buffer = sb.ToString();
        var ptr = Marshal.StringToHGlobalUni(buffer);
        
        try 
        {
            var shf = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = ptr,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
            };

            var result = SHFileOperation(ref shf);
            if (result != 0)
            {
                throw new Exception($"Error code: {result}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom; // Changed from string to IntPtr
        public string pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_SILENT = 0x0004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);
}
