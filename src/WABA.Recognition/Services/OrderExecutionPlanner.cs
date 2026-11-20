using WABA.Domain.Models;

namespace WABA.Recognition.Services;

/// <summary>
/// Builds a conservative, reviewable click plan from the latest recognized order
/// and the locked 37-slot shelf catalogue. This class never sends input.
/// </summary>
public sealed class OrderExecutionPlanner
{
    public const int ExpectedCatalogCount = 37;
    public const int MaximumClicksPerPlan = 20;
    public const int MaximumQuantityPerProduct = 10;

    public OrderExecutionPlan BuildPlan(
        IReadOnlyList<RecognizedOrderItem> orderItems,
        IReadOnlyList<ShelfItemDetection> catalog,
        bool catalogLocked,
        bool layoutMatches,
        int frameWidth,
        int frameHeight)
    {
        var issues = new List<string>();
        var steps = new List<OrderExecutionStep>();

        if (!catalogLocked)
            issues.Add("Katalog belum terkunci. Kunci katalog sebelum membuat rencana eksekusi.");

        if (!layoutMatches)
            issues.Add("Rasio layar game berbeda dari baseline katalog. Scan ulang katalog pada ukuran jendela saat ini.");

        if (catalog.Count != ExpectedCatalogCount)
            issues.Add($"Katalog tidak lengkap: {catalog.Count}/{ExpectedCatalogCount} slot.");

        if (frameWidth <= 0 || frameHeight <= 0)
            issues.Add("Ukuran frame game tidak valid.");

        if (orderItems.Count == 0)
            issues.Add("Belum ada item pesanan yang terbaca.");

        var duplicateNames = orderItems
            .Where(item => item.IsKnownProduct)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        foreach (var duplicate in duplicateNames)
            issues.Add($"Nama produk muncul lebih dari sekali: {duplicate}. Periksa OCR sebelum melanjutkan.");

        foreach (var order in orderItems)
        {
            if (!order.IsKnownProduct)
            {
                issues.Add($"Produk belum dikenali OCR: \"{order.Name}\".");
                continue;
            }

            if (!order.CompletedQuantity.HasValue || !order.RequestedQuantity.HasValue)
            {
                issues.Add($"Kuantitas {order.Name} belum terbaca dengan pasti.");
                continue;
            }

            var remaining = order.RequestedQuantity.Value - order.CompletedQuantity.Value;
            if (remaining < 0 || order.RequestedQuantity.Value <= 0)
            {
                issues.Add($"Kuantitas {order.Name} tidak valid ({order.CompletedQuantity}/{order.RequestedQuantity}).");
                continue;
            }

            if (remaining == 0)
                continue;

            if (remaining > MaximumQuantityPerProduct)
            {
                issues.Add($"Jumlah {order.Name} terlalu besar untuk satu rencana ({remaining}). Periksa OCR.");
                continue;
            }

            var matches = catalog
                .Where(item => string.Equals(
                    item.ProductName, order.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length != 1)
            {
                issues.Add(matches.Length == 0
                    ? $"Koordinat {order.Name} tidak ditemukan dalam katalog."
                    : $"Koordinat {order.Name} tidak unik dalam katalog.");
                continue;
            }

            var target = matches[0];
            if (target.ClientX < 0 || target.ClientY < 0 ||
                target.ClientX >= frameWidth || target.ClientY >= frameHeight)
            {
                issues.Add($"Koordinat {order.Name} berada di luar area game.");
                continue;
            }

            steps.Add(new OrderExecutionStep(
                order.Name,
                remaining,
                target.ClientX,
                target.ClientY,
                target.Area,
                target.Confidence));
        }

        var totalClicks = steps.Sum(step => step.Quantity);
        if (totalClicks > MaximumClicksPerPlan)
            issues.Add($"Rencana berisi {totalClicks} klik; batas aman saat ini {MaximumClicksPerPlan}. Periksa hasil OCR.");

        if (steps.Count == 0 && issues.Count == 0)
            issues.Add("Tidak ada item yang masih perlu diambil.");

        return new OrderExecutionPlan(
            steps,
            totalClicks,
            issues.Count == 0 && steps.Count > 0,
            issues,
            DateTimeOffset.UtcNow);
    }
}

public sealed record OrderExecutionStep(
    string ProductName,
    int Quantity,
    int ClientX,
    int ClientY,
    string Area,
    double Confidence);

public sealed record OrderExecutionPlan(
    IReadOnlyList<OrderExecutionStep> Steps,
    int TotalClicks,
    bool IsReady,
    IReadOnlyList<string> Issues,
    DateTimeOffset CreatedAtUtc);
