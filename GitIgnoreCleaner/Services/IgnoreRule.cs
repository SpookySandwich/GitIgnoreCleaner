using System.Text.RegularExpressions;

namespace GitIgnoreCleaner.Services;

public sealed class IgnoreRule
{
    private readonly Regex _pathRegex;
    private readonly Regex _nameRegex;

    public IgnoreRule(string pattern, string basePath, string sourceFile, bool isNegation, bool directoryOnly, bool anchored)
    {
        Pattern = pattern;
        BasePath = basePath;
        SourceFile = sourceFile;
        IsNegation = isNegation;
        DirectoryOnly = directoryOnly;
        Anchored = anchored;
        HasSlash = pattern.Contains('/');
        _pathRegex = new Regex("^" + GlobToRegex(pattern) + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        _nameRegex = HasSlash
            ? _pathRegex
            : new Regex("^" + GlobToRegex(pattern) + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public string Pattern { get; }
    public string BasePath { get; }
    public string SourceFile { get; }
    public bool IsNegation { get; }
    public bool DirectoryOnly { get; }
    public bool Anchored { get; }
    public bool HasSlash { get; }

    public bool Matches(string fullPath, bool isDirectory)
    {
        var relativePath = Path.GetRelativePath(BasePath, fullPath)
            .Replace('\\', '/');

        if (relativePath == ".")
        {
            relativePath = string.Empty;
        }

        if (DirectoryOnly && !isDirectory)
        {
            return MatchesAncestorDirectory(relativePath);
        }

        return MatchesPath(relativePath);
    }

    private bool MatchesAncestorDirectory(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1)
        {
            return false;
        }

        if (!Anchored && !HasSlash)
        {
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (_nameRegex.IsMatch(segments[i]))
                {
                    return true;
                }
            }

            return false;
        }

        var current = segments[0];
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (i > 0)
            {
                current = current + "/" + segments[i];
            }

            if (_pathRegex.IsMatch(current))
            {
                return true;
            }
        }

        return _pathRegex.IsMatch(current);
    }

    private bool MatchesPath(string relativePath)
    {
        if (Anchored || HasSlash)
        {
            return _pathRegex.IsMatch(relativePath);
        }

        var name = Path.GetFileName(relativePath);
        return _nameRegex.IsMatch(name);
    }

    private static string GlobToRegex(string pattern)
    {
        var regex = new System.Text.StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    var isDoubleStar = i + 1 < pattern.Length && pattern[i + 1] == '*';
                    if (isDoubleStar)
                    {
                        regex.Append(".*");
                        i++;
                    }
                    else
                    {
                        regex.Append("[^/]*");
                    }
                    break;
                case '?':
                    regex.Append("[^/]");
                    break;
                case '.':
                    regex.Append("\\.");
                    break;
                case '/':
                    regex.Append('/');
                    break;
                case '\\':
                    regex.Append("\\\\");
                    break;
                case '+':
                case '(':
                case ')':
                case '^':
                case '$':
                case '|':
                case '{':
                case '}':
                case '[':
                case ']':
                    regex.Append('\\').Append(c);
                    break;
                default:
                    regex.Append(c);
                    break;
            }
        }

        return regex.ToString();
    }
}

public sealed class IgnoreRuleStack
{
    private readonly List<IgnoreRule> _rules = [];

    public int Count => _rules.Count;

    public void AddRange(IEnumerable<IgnoreRule> rules)
    {
        _rules.AddRange(rules);
    }

    public void RemoveLast(int count)
    {
        if (count <= 0)
        {
            return;
        }

        _rules.RemoveRange(_rules.Count - count, count);
    }

    public (bool IsIgnored, IgnoreRule? MatchedRule, List<IgnoreRule> AllMatchedRules) CheckIgnored(string fullPath, bool isDirectory)
    {
        bool? ignored = null;
        IgnoreRule? lastMatchedRule = null;
        var allMatchedRules = new List<IgnoreRule>();

        foreach (var rule in _rules)
        {
            if (rule.Matches(fullPath, isDirectory))
            {
                ignored = !rule.IsNegation;
                lastMatchedRule = rule;
                allMatchedRules.Add(rule);
            }
        }

        return (ignored ?? false, ignored == true ? lastMatchedRule : null, allMatchedRules);
    }

    public bool IsIgnored(string fullPath, bool isDirectory)
    {
        return CheckIgnored(fullPath, isDirectory).IsIgnored;
    }
}

