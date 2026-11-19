using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WABA.Recognition.Services;

/// <summary>
/// Matches short shelf captions to the canonical game catalogue. OCR-specific
/// aliases are explicit so compact labels such as "2GB" and common glyph
/// confusions do not have to rely on a permissive fuzzy threshold.
/// </summary>
internal static class ProductLabelMatcher
{
    private static readonly IReadOnlyDictionary<string, string[]> Aliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Gula"] = ["gu1a", "guia", "gu la"],
            ["Galon"] = ["ga1on", "gaion", "gal0n"],
            ["Teh celup"] = ["tehce1up", "teh cel up", "tehcelup"],
            ["Teh botol"] = ["teh botoi", "tehbotol"],
            ["E-Wallet"] = ["ewallet", "e wallet", "e-wallet", "e waliet"],
            ["2 GB"] = ["2gb", "2 gb"],
            ["5 GB"] = ["5gb", "5 gb"],
            ["10 GB"] = ["10gb", "10 gb"],
            ["Token listrik"] = ["tokenlistrik", "token listrik"],
            ["Mie rebus"] = ["mi rebus", "mi-rebus"],
            ["Mie goreng"] = ["mi goreng", "mi-goreng"],
            ["Air mineral"] = ["airmineral"],
            ["Kopi sachet"] = ["kopisachet"],
            ["Kopi susu"] = ["kopisusu"],
            ["Makanan kucing"] = ["makanankucing"],
            ["Minyak goreng"] = ["minyakgoreng"],
            ["Sabun mandi"] = ["sabunmandi"],
            ["Susu kotak"] = ["sus kotak", "susukotak"],
            ["Minuman energi"] = ["minumanenergi"],
            ["Buku tulis"] = ["bukutulis", "buk tulis", "buk utulis"],
            ["Rengginang"] = ["rengginanq"],
            ["Kerupuk"] = ["krupuk", "kerup uk"],
            ["Deterjen"] = ["detergen", "deterjen"],
        };

    public static (string? Name, double Confidence) Match(string label)
    {
        var normalized = Normalize(label);
        if (normalized.Length < 3)
            return (null, 0d);

        var compact = Compact(normalized);
        string? bestName = null;
        var bestScore = 0d;

        foreach (var name in OrderTextParser.KnownProductNames)
        {
            IEnumerable<string> forms = Aliases.TryGetValue(name, out var aliases)
                ? aliases.Prepend(name)
                : new[] { name };

            foreach (var form in forms)
            {
                var candidate = Normalize(form);
                var candidateCompact = Compact(candidate);
                if (normalized == candidate || compact == candidateCompact)
                    return (name, 1d);

                // Accept a canonical caption surrounded by small OCR noise, but
                // avoid matching short fragments such as "2 GB" inside prices.
                if (normalized.Contains(candidate, StringComparison.Ordinal) && candidate.Length >= 4)
                {
                    var score = Math.Min(0.98d, (double)candidate.Length / normalized.Length);
                    if (score > bestScore)
                    {
                        bestName = name;
                        bestScore = score;
                    }
                }

                var normalizedScore = Similarity(normalized, candidate);
                var compactScore = Similarity(compact, candidateCompact);
                var scoreFuzzy = Math.Max(normalizedScore, compactScore);
                if (scoreFuzzy > bestScore)
                {
                    bestName = name;
                    bestScore = scoreFuzzy;
                }
            }
        }

        // The threshold remains conservative. Explicit aliases above handle
        // known OCR substitutions without broadly accepting unrelated labels.
        return bestScore >= 0.80d ? (bestName, bestScore) : (null, bestScore);
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

    private static string Compact(string value)
        => value.Replace(" ", string.Empty, StringComparison.Ordinal);

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
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return 1d - (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }
}
