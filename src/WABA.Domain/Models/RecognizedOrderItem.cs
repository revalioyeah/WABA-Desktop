namespace WABA.Domain.Models;

public sealed record RecognizedOrderItem(
    string Name,
    int? CompletedQuantity,
    int? RequestedQuantity,
    string RawLine,
    bool IsKnownProduct)
{
    public int? RemainingQuantity =>
        CompletedQuantity.HasValue && RequestedQuantity.HasValue
            ? Math.Max(0, RequestedQuantity.Value - CompletedQuantity.Value)
            : null;
}
