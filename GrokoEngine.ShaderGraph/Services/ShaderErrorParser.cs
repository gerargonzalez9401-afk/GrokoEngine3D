using System.Text.RegularExpressions;

namespace GrokoShaderGraphPro.Services;

public static class ShaderErrorParser
{
    private static readonly Regex GlslError = new(
        @"(?<kind>ERROR|WARNING):\s*(?<line>\d+):(?<subline>\d+)?:?\s*(?<message>.+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string FormatForEditor(string rawLog)
    {
        if (string.IsNullOrWhiteSpace(rawLog))
            return string.Empty;

        var lines = rawLog.Split(
            new[] { "\r\n", "\n" },
            StringSplitOptions.RemoveEmptyEntries);

        var pretty = new List<string>();

        foreach (var line in lines)
        {
            var m = GlslError.Match(line);

            if (m.Success)
            {
                pretty.Add(
                    "[" + m.Groups["kind"].Value.ToUpperInvariant() + "] line " +
                    m.Groups["line"].Value + ": " +
                    m.Groups["message"].Value.Trim());
            }
            else
            {
                pretty.Add(line.Trim());
            }
        }

        return string.Join(Environment.NewLine, pretty.Distinct());
    }
}
