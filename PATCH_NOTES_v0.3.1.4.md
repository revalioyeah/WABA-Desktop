# WABA Desktop v0.3.1.4 — Targeted ROI OCR fallback

Based on the uploaded `WABA-Desktop-main` source (v0.3.1.3 with the previous CS0266 compile fix already present).

## Changes

- Adds a dedicated, compact 4x OCR retry for the `Gula` caption when the normal left-shelf scan does not find it.
- Adds independent 4x OCR slot crops for missing `2 GB` and `5 GB` counter cards.
- Adds counter aliases for common OCR substitutions including `2G8`, `50B`, `5OB`, and `S0B`.
- Uses client-area-relative slot positions for the current five-card counter layout.
- Uses the voucher artwork centre for provisional counter points instead of subtracting a fixed offset from an unmatched OCR token.
- Keeps all fallback markers below the green/validated threshold and preview-only.
- Does not modify Phase 2 order OCR, auto scan cadence, mouse input, or order execution.

## Performance

The ordinary shelf scan is unchanged. The extra OCR calls run only for missing products: at most one Gula crop and two counter-slot crops. These are not run during the Phase 2 order-panel OCR loop.

## Validation

- The uploaded source already contains the explicit `double` cast in the counter spacing calculation that fixed CS0266.
- Source structure and targeted ROI geometry were inspected against the supplied 1767x992 screenshot.
- .NET SDK is not installed in this editing environment, so a local `dotnet publish` was not performed. Confirm compilation using GitHub Actions.
- Verify the orange candidate markers visually. Text-match confidence is not coordinate accuracy.

## Expected current catalogue

- LEFT_SHELF: 16 items, including Gula.
- RIGHT_SHELF: 16 items.
- PULSE_COUNTER: 5 items, including E-Wallet, 2 GB, 5 GB, 10 GB, Token listrik.

Total expected catalogue: 37 products.
