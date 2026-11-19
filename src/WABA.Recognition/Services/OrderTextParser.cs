using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WABA.Domain.Models;

namespace WABA.Recognition.Services;

/// <summary>
/// Converts OCR lines into conservative order records.
/// Unknown labels are retained as unknown and must never be auto-clicked.
/// </summary>
public sealed class OrderTextParser
{
    private static readonly Regex QuantityPattern = new(
        @"(?<done>[0-9OIl]{1,2})\s*[/|\\]\s*(?<total>[0-9OIl]{1,2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] ProductNames =
    [
        "Minuman energi", "Makanan kucing", "Minyak goreng", "Sabun mandi",
        "Air mineral", "Kopi sachet", "Kopi susu", "Mie goreng", "Mie rebus",
        "Token listrik", "Susu kotak", "Buku tulis", "Teh celup", "Teh botol",
        "LPG 3 kg", "Rengginang", "Tisu", "Beras", "Telur", "Sayuran", "Obat", "Galon",
        "Gula", "Deterjen", "Ciki", "Permen", "Biskuit", "Roti", "Jamu",
        "Kerupuk", "Soda", "Es krim", "Kurma", "Sirup", "E-Wallet", "2 GB",
        "5 GB", "10 GB"
    ];

    private static readonly string[] NormalizedProducts =
        ProductNames
            .OrderByDescending(x => x.Length)
            .Select(Normalize)
            .ToArray();

    public IReadOnlyList<RecognizedOrderItem> Parse(IEnumerable<string> ocrLines)
    {
        var parsed = new List<RecognizedOrderItem>();

        foreach (var rawLine in ocrLines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            var match = QuantityPattern.Match(rawLine);
            var labelText = QuantityPattern.Replace(rawLine, " ");
            var normalizedLine = Normalize(labelText);
            var productIndex = FindProductIndex(normalizedLine);

            int? done = null;
            int? total = null;

            if (match.Success)
            {
                done = ParseOcrDigit(match.Groups["done"].Value);
                total = ParseOcrDigit(match.Groups["total"].Value);
                if (done.HasValue && total.HasValue && done.Value > total.Value)
                {
                    done = null;
                    total = null;
                }
            }

            if (productIndex >= 0)
            {
                var name = ProductNames[productIndex];
                if (!parsed.Any(x =>
                        string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) &&
                        x.CompletedQuantity == done &&
                        x.RequestedQuantity == total))
                {
                    parsed.Add(new RecognizedOrderItem(name, done, total, rawLine.Trim(), true));
                }
            }
            else if (match.Success)
            {
                var prefix = QuantityPattern.Replace(rawLine, "").Trim(' ', ':', '-', '|', '/', '.');
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    parsed.Add(new RecognizedOrderItem(prefix, done, total, rawLine.Trim(), false));
                }
            }
        }

        return parsed;
    }

    private static int FindProductIndex(string normalizedLine)
    {
        for (var i = 0; i < NormalizedProducts.Length; i++)
        {
            if (normalizedLine.Contains(NormalizedProducts[i], StringComparison.Ordinal))
            {
                return Array.FindIndex(ProductNames, x =>
                    string.Equals(Normalize(x), NormalizedProducts[i], StringComparison.Ordinal));
            }
        }

        // Conservative fuzzy fallback for common one-character OCR mistakes
        // (for example "rengginag" -> "rengginang"). Short labels are exact-match only.
        var bestIndex = -1;
        var bestScore = 0.0;
        for (var i = 0; i < NormalizedProducts.Length; i++)
        {
            var candidate = NormalizedProducts[i];
            if (candidate.Length < 6 || normalizedLine.Length < 6)
                continue;

            var score = Similarity(normalizedLine, candidate);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = Array.FindIndex(ProductNames, x =>
                    string.Equals(Normalize(x), candidate, StringComparison.Ordinal));
            }
        }

        return bestScore >= 0.84 ? bestIndex : -1;
    }

    private static double Similarity(string left, string right)
    {
        var distance = EditDistance(left, right);
        return 1.0 - (double)distance / Math.Max(left.Length, right.Length);
    }

    private static int EditDistance(string left, string right)
    {
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

        return previous[right.Length];
    }

    private static int? ParseOcrDigit(string value)
    {
        var corrected = value
            .Replace('O', '0').Replace('o', '0')
            .Replace('I', '1').Replace('l', '1');
        return int.TryParse(corrected, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }
}
