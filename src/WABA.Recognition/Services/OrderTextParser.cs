using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WABA.Domain.Models;
using WABA.Recognition.Models;

namespace WABA.Recognition.Services;

/// <summary>
/// Converts OCR words into conservative order records.
/// Product labels and quantities are associated by bounding-box row, not by
/// assuming Windows OCR always returns both parts in the same text line.
/// Unknown labels remain unknown and are never eligible for automatic input.
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

    // OCR is performed on a 2x crop. A small vertical tolerance joins words
    // from the same visual row while keeping adjacent order rows separate.
    private const double MinimumRowTolerance = 7d;
    private const double MaximumRowTolerance = 19d;
    private const double RowHeightToleranceFactor = 0.70d;

    private sealed class WordRow
    {
        public List<OcrWordFragment> Words { get; } = [];

        public double CenterY => Words.Average(word => word.CenterY);
        public double AverageHeight => Words.Average(word => Math.Max(1d, word.Height));

        public void Add(OcrWordFragment word) => Words.Add(word);

        public string Text => string.Join(" ",
            Words.OrderBy(word => word.X)
                .Select(word => word.Text.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    /// <summary>
    /// Preferred parser entry point. OCR word positions are used to reconstruct
    /// visual rows, so a quantity can be paired even if OCR split it into a
    /// separate line from the product name.
    /// </summary>
    public IReadOnlyList<RecognizedOrderItem> ParseWords(IEnumerable<OcrWordFragment> source)
    {
        var words = source
            .Where(word =>
                !string.IsNullOrWhiteSpace(word.Text) &&
                word.Width > 0 &&
                word.Height > 0)
            .OrderBy(word => word.CenterY)
            .ThenBy(word => word.X)
            .ToArray();

        if (words.Length == 0)
            return [];

        var rows = BuildRows(words);
        var parsed = new List<RecognizedOrderItem>();

        foreach (var row in rows)
        {
            var rowText = row.Text;
            if (string.IsNullOrWhiteSpace(rowText))
                continue;

            var quantityMatch = QuantityPattern.Match(rowText);
            var productIndex = FindProductIndex(
                quantityMatch.Success
                    ? QuantityPattern.Replace(rowText, " ")
                    : rowText);

            int? completed = null;
            int? requested = null;

            if (quantityMatch.Success)
            {
                completed = ParseOcrDigit(quantityMatch.Groups["done"].Value);
                requested = ParseOcrDigit(quantityMatch.Groups["total"].Value);

                if (completed.HasValue &&
                    requested.HasValue &&
                    (completed.Value > requested.Value || requested.Value == 0))
                {
                    completed = null;
                    requested = null;
                }
            }

            if (productIndex >= 0)
            {
                var name = ProductNames[productIndex];
                AddUnique(parsed, new RecognizedOrderItem(
                    name, completed, requested, rowText.Trim(), true));
                continue;
            }

            // Preserve an unrecognized label if a quantity was found on the row.
            if (quantityMatch.Success)
            {
                var prefix = QuantityPattern.Replace(rowText, " ")
                    .Trim(' ', ':', '-', '|', '/', '.', '•');
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    AddUnique(parsed, new RecognizedOrderItem(
                        prefix, completed, requested, rowText.Trim(), false));
                }
            }
        }

        return parsed;
    }

    /// <summary>
    /// Compatibility parser for callers that only have OCR text lines.
    /// Quantity pairing in this overload is intentionally limited to one line.
    /// Use ParseWords whenever bounding boxes are available.
    /// </summary>
    public IReadOnlyList<RecognizedOrderItem> Parse(IEnumerable<string> ocrLines)
    {
        var parsed = new List<RecognizedOrderItem>();

        foreach (var rawLine in ocrLines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            var match = QuantityPattern.Match(rawLine);
            var labelText = QuantityPattern.Replace(rawLine, " ");
            var productIndex = FindProductIndex(labelText);

            int? completed = null;
            int? requested = null;

            if (match.Success)
            {
                completed = ParseOcrDigit(match.Groups["done"].Value);
                requested = ParseOcrDigit(match.Groups["total"].Value);

                if (completed.HasValue &&
                    requested.HasValue &&
                    (completed.Value > requested.Value || requested.Value == 0))
                {
                    completed = null;
                    requested = null;
                }
            }

            if (productIndex >= 0)
            {
                AddUnique(parsed, new RecognizedOrderItem(
                    ProductNames[productIndex], completed, requested, rawLine.Trim(), true));
            }
            else if (match.Success)
            {
                var prefix = QuantityPattern.Replace(rawLine, " ")
                    .Trim(' ', ':', '-', '|', '/', '.', '•');
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    AddUnique(parsed, new RecognizedOrderItem(
                        prefix, completed, requested, rawLine.Trim(), false));
                }
            }
        }

        return parsed;
    }

    private static List<WordRow> BuildRows(IReadOnlyList<OcrWordFragment> words)
    {
        var rows = new List<WordRow>();

        foreach (var word in words)
        {
            WordRow? bestRow = null;
            var bestDistance = double.MaxValue;

            foreach (var row in rows)
            {
                var distance = Math.Abs(word.CenterY - row.CenterY);
                var referenceHeight = Math.Min(
                    Math.Max(1d, word.Height),
                    Math.Max(1d, row.AverageHeight));
                var tolerance = Math.Clamp(
                    referenceHeight * RowHeightToleranceFactor,
                    MinimumRowTolerance,
                    MaximumRowTolerance);

                if (distance <= tolerance && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestRow = row;
                }
            }

            if (bestRow is null)
            {
                var newRow = new WordRow();
                newRow.Add(word);
                rows.Add(newRow);
            }
            else
            {
                bestRow.Add(word);
            }
        }

        return rows.OrderBy(row => row.CenterY).ToList();
    }

    private static void AddUnique(
        ICollection<RecognizedOrderItem> parsed,
        RecognizedOrderItem candidate)
    {
        if (parsed.Any(existing =>
                string.Equals(existing.Name, candidate.Name, StringComparison.OrdinalIgnoreCase) &&
                existing.CompletedQuantity == candidate.CompletedQuantity &&
                existing.RequestedQuantity == candidate.RequestedQuantity))
        {
            return;
        }

        parsed.Add(candidate);
    }

    private static int FindProductIndex(string text)
    {
        var normalizedLine = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalizedLine))
            return -1;

        for (var i = 0; i < NormalizedProducts.Length; i++)
        {
            if (normalizedLine.Contains(NormalizedProducts[i], StringComparison.Ordinal))
                return ProductIndexForNormalized(NormalizedProducts[i]);
        }

        // Compare small token windows rather than the entire OCR row. This
        // tolerates leading/trailing OCR noise and one-character label errors.
        var lineTokens = normalizedLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bestIndex = -1;
        var bestScore = 0d;

        foreach (var candidate in NormalizedProducts)
        {
            var candidateTokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (candidate.Length < 6 || lineTokens.Length == 0)
                continue;

            var minimumLength = Math.Max(1, candidateTokens.Length - 1);
            var maximumLength = Math.Min(lineTokens.Length, candidateTokens.Length + 1);

            for (var windowLength = minimumLength; windowLength <= maximumLength; windowLength++)
            {
                for (var start = 0; start + windowLength <= lineTokens.Length; start++)
                {
                    var window = string.Join(" ", lineTokens.Skip(start).Take(windowLength));
                    var score = Similarity(window, candidate);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = ProductIndexForNormalized(candidate);
                    }
                }
            }

            // Some OCR engines omit spaces between short words.
            var compactLine = normalizedLine.Replace(" ", "", StringComparison.Ordinal);
            var compactCandidate = candidate.Replace(" ", "", StringComparison.Ordinal);
            if (compactLine.Contains(compactCandidate, StringComparison.Ordinal))
                return ProductIndexForNormalized(candidate);
        }

        return bestScore >= 0.84d ? bestIndex : -1;
    }

    private static int ProductIndexForNormalized(string normalized)
    {
        for (var index = 0; index < ProductNames.Length; index++)
        {
            if (string.Equals(Normalize(ProductNames[index]), normalized, StringComparison.Ordinal))
                return index;
        }

        return -1;
    }

    private static double Similarity(string left, string right)
    {
        var distance = EditDistance(left, right);
        return 1d - (double)distance / Math.Max(left.Length, right.Length);
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
            .Replace('I', '1').Replace('i', '1')
            .Replace('l', '1');
        return int.TryParse(
            corrected,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var number)
            ? number
            : null;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : ' ');
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }
}
