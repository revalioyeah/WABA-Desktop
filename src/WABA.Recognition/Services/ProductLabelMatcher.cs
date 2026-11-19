using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WABA.Recognition.Services;

internal static class ProductLabelMatcher
{
    public static (string? Name, double Confidence) Match(string label)
    {
        var normalized = Normalize(label);
        if (normalized.Length < 3)
            return (null, 0d);

        string? bestName = null;
        var bestScore = 0d;

        foreach (var name in OrderTextParser.KnownProductNames)
        {
            var candidate = Normalize(name);
            if (normalized.Contains(candidate, StringComparison.Ordinal) ||
                candidate.Contains(normalized, StringComparison.Ordinal))
            {
                var score = normalized == candidate ? 1d : Math.Min(0.98d, (double)Math.Min(normalized.Length, candidate.Length) / Math.Max(normalized.Length, candidate.Length));
                if (score > bestScore)
                {
                    bestName = name;
                    bestScore = score;
                }
                continue;
            }

            var scoreFuzzy = Similarity(normalized, candidate);
            if (scoreFuzzy > bestScore)
            {
                bestName = name;
                bestScore = scoreFuzzy;
            }
        }

        return bestScore >= 0.78d ? (bestName, bestScore) : (null, bestScore);
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    private static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
            return 0d;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return 1d - (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }
}
