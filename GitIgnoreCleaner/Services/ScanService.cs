using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GitIgnoreCleaner.Models;
using Microsoft.UI.Dispatching;

namespace GitIgnoreCleaner.Services;

public sealed class ScanResult
{
    public ScanNode? RootNode { get; set; }
    public List<string> Errors { get; } = [];
    public int CandidateCount { get; set; }
    public long TotalBytes { get; set; }
}

public sealed class ScanService
{
    public Task<ScanResult> ScanAsync(
        string rootPath,
        IReadOnlyList<string> ignoreFileNames,
        CancellationToken cancellationToken,
        ObservableCollection<ScanNode>? rootChildren = null,
        Action<ScanNode>? onRootCreated = null,
        Action<int>? onProgress = null,
        DispatcherQueue? dispatcher = null)
    {
        return Task.Run(() =>
        {
            var result = new ScanResult();
            var rules = new IgnoreRuleStack();
            var ignoreSet = new HashSet<string>(ignoreFileNames, StringComparer.OrdinalIgnoreCase);

            var rootNode = new ScanNode(rootPath, rootPath, isDirectory: true, isCandidate: false, children: rootChildren);

            if (onRootCreated != null && dispatcher != null)
            {
                dispatcher.TryEnqueue(() => onRootCreated(rootNode));
            }

            int scannedCount = 0;
            Action reportProgress = () =>
            {
                scannedCount++;
                if (onProgress != null && dispatcher != null && scannedCount % 50 == 0)
                {
                    dispatcher.TryEnqueue(() => onProgress(scannedCount));
                }
            };

            ScanDirectory(rootPath, rules, ignoreSet, result, cancellationToken, isRoot: true, () => rootNode, dispatcher, reportProgress);

            if (onProgress != null && dispatcher != null)
            {
                dispatcher.TryEnqueue(() => onProgress(scannedCount));
            }

            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() => rootNode.RecalculateSize());
            }
            else
            {
                rootNode.RecalculateSize();
            }

            // Compact the tree to flatten single-child directories
            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() => rootNode.Compact());
            }
            else
            {
                rootNode.Compact();
            }

            result.RootNode = rootNode;
            return result;
        }, cancellationToken);
    }

    private bool ScanDirectory(
        string directoryPath,
        IgnoreRuleStack rules,
        HashSet<string> ignoreFileNames,
        ScanResult result,
        CancellationToken cancellationToken,
        bool isRoot,
        Func<ScanNode> getParentNode,
        DispatcherQueue? dispatcher,
        Action reportProgress)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var addedRuleCount = LoadIgnoreRules(directoryPath, rules, result, ignoreFileNames);

        var (isDirIgnored, dirMatchedRule, dirAllRules) = !isRoot
            ? rules.CheckIgnored(directoryPath, isDirectory: true)
            : (false, null, new List<IgnoreRule>());

        var containsUnignored = isRoot ? false : !isDirIgnored;

        ScanNode? myNode = null;

        ScanNode GetMyNode()
        {
            if (myNode != null) return myNode;

            if (isRoot)
            {
                myNode = getParentNode();
            }
            else
            {
                var displayName = Path.GetFileName(directoryPath);
                var dirSource = dirMatchedRule != null ? $"{Path.GetFileName(dirMatchedRule.SourceFile)}: {dirMatchedRule.Pattern}" : string.Empty;
                var dirRulePaths = dirAllRules.Select(r => r.SourceFile).Distinct().ToList();

                myNode = new ScanNode(displayName, directoryPath, isDirectory: true, isCandidate: false, matchedRuleSource: dirSource, ignoreRulePaths: dirRulePaths);

                var parent = getParentNode();

                if (dispatcher != null)
                {
                    dispatcher.TryEnqueue(() => parent.AddChild(myNode));
                }
                else
                {
                    parent.AddChild(myNode);
                }
            }
            return myNode;
        }

        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(directoryPath);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Failed to read {directoryPath}: {ex.Message}");
            rules.RemoveLast(addedRuleCount);
            return true;
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reportProgress();

            var name = Path.GetFileName(entry);
            if (ignoreFileNames.Contains(name)) continue;

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to inspect {entry}: {ex.Message}");
                containsUnignored = true;
                continue;
            }

            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            var isReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);
            var shouldRecurse = isDirectory;

            if (isDirectory && isReparsePoint)
            {
                try
                {
                    var info = new DirectoryInfo(entry);
                    if (info.LinkTarget != null) shouldRecurse = false;
                }
                catch { shouldRecurse = false; }
            }

            if (shouldRecurse)
            {
                var childContainsUnignored = ScanDirectory(entry, rules, ignoreFileNames, result, cancellationToken, isRoot: false, GetMyNode, dispatcher, reportProgress);
                if (childContainsUnignored)
                {
                    containsUnignored = true;
                }
                continue;
            }

            var (isIgnored, matchedRule, allRules) = rules.CheckIgnored(entry, isDirectory);
            if (!isIgnored)
            {
                containsUnignored = true;
                continue;
            }

            long size = 0;
            if (!isDirectory)
            {
                try { size = new FileInfo(entry).Length; }
                catch (Exception ex) { result.Errors.Add($"Failed to read size for {entry}: {ex.Message}"); }
            }

            var source = matchedRule != null ? $"{Path.GetFileName(matchedRule.SourceFile)}: {matchedRule.Pattern}" : string.Empty;
            var rulePaths = allRules.Select(r => r.SourceFile).Distinct().ToList();

            var fileNode = new ScanNode(name, entry, isDirectory, isCandidate: true, sizeBytes: size, matchedRuleSource: source, ignoreRulePaths: rulePaths);

            var parent = GetMyNode();

            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() => parent.AddChild(fileNode));
            }
            else
            {
                parent.AddChild(fileNode);
            }

            result.CandidateCount++;
            result.TotalBytes += size;
        }

        rules.RemoveLast(addedRuleCount);

        if (isDirIgnored && !containsUnignored)
        {
            var node = GetMyNode();

            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() =>
                {
                    node.SetIsCandidate(true);
                });
            }
            else
            {
                node.SetIsCandidate(true);
            }

            result.CandidateCount++;
        }

        return containsUnignored;
    }

    private static int LoadIgnoreRules(
        string directoryPath,
        IgnoreRuleStack rules,
        ScanResult result,
        HashSet<string> ignoreFileNames)
    {
        var addedRuleCount = 0;

        foreach (var ignoreFileName in ignoreFileNames)
        {
            var ignorePath = Path.Combine(directoryPath, ignoreFileName);
            if (!File.Exists(ignorePath)) continue;

            try
            {
                var attributes = File.GetAttributes(ignorePath);
                if (attributes.HasFlag(FileAttributes.Offline)) continue;

                var fileRules = IgnoreFileParser.ParseFile(ignorePath, directoryPath);
                rules.AddRange(fileRules);
                addedRuleCount += fileRules.Count;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to read ignore file {ignorePath}: {ex.Message}");
            }
        }

        return addedRuleCount;
    }
}
