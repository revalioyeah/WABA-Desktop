# WABA Desktop v0.3.1.5.1 — Compile Fix

## Fixed
- `ShelfItemDetector.cs`: explicitly declare `spacingBetweenTenAndToken` as `double`.
- Fixes CS0266 at the relational-pattern check for the 65d and 145d bounds.

## Behavior
- No change to product catalog, anchor coordinates, OCR thresholds, or gesture behavior.
- Integer client coordinates are promoted to double for spacing validation.

## Validation
- ZIP integrity checked. Run GitHub Actions to confirm `dotnet publish`.
