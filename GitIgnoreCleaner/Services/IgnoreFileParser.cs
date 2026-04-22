namespace GitIgnoreCleaner.Services;

public static class IgnoreFileParser
{
    public static IgnoreRuleLayer ParseFile(string filePath)
    {
        var rules = new List<IgnorePatternRule>();
        foreach (var line in File.ReadLines(filePath))
        {
            var rule = IgnorePatternRule.Parse(line, filePath);
            if (rule != null)
            {
                rules.Add(rule);
            }
        }

        var baseDirectory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException(LocalizationService.GetString("ErrorIgnoreFileNoParent"));
        return new IgnoreRuleLayer(filePath, baseDirectory, rules);
    }
}
