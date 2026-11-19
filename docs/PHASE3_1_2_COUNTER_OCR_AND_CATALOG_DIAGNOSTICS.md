# WABA Desktop v0.3.1.2 — Counter OCR & Catalog Diagnostics

## Why this patch exists

The latest user-reported detection run found 33 products: 15 on `LEFT_SHELF`, 16 on `RIGHT_SHELF`, and 2 on `PULSE_COUNTER`. The expected calibrated catalogue for the current visible layout is 37 products: 16 left, 16 right, and 5 counter. The four not found were `Gula`, `E-Wallet`, `2 GB`, and `5 GB`.

The counter diagnostics included the raw OCR fragment `E-WaUet`, which indicates a glyph substitution rather than a completely absent caption.

## Changes

- Added explicit `E-WaUet` / `EWaUet` aliases for `E-Wallet` without reducing the global fuzzy-match threshold.
- Added counter-only aliases for common `2/Z` and `5/S` substitutions in `2 GB` and `5 GB`.
- Increased only the small pulse-counter crop from 3x to 4x. Main shelves remain at 2x to limit added OCR cost.
- Relaxed vertical token grouping and horizontal fragment grouping only for the pulse counter, so split number/unit tokens can form one caption.
- Added per-area expected-catalogue diagnostics. Missing products are reported as `Katalog belum terdeteksi`; no coordinates are fabricated.
- Updated the coordinate reference CSV with the latest 33 user-reported observations at 1831x1028. Scores are text-match scores only and coordinates remain unverified for clicking.

## Expected catalogue for the current 37-product layout

- `LEFT_SHELF` (16): Mie rebus, Telur, Mie goreng, Kopi sachet, Beras, Buku tulis, Makanan kucing, Sayuran, Obat, Galon, LPG 3 kg, Gula, Minyak goreng, Sabun mandi, Teh celup, Deterjen.
- `RIGHT_SHELF` (16): Ciki, Permen, Biskuit, Air mineral, Roti, Jamu, Teh botol, Kerupuk, Minuman energi, Soda, Rengginang, Kopi susu, Susu kotak, Es krim, Kurma, Sirup.
- `PULSE_COUNTER` (5): E-Wallet, 2 GB, 5 GB, 10 GB, Token listrik.

`Tisu` remains present in the order-recognition vocabulary but is not included in this 37-item coordinate baseline because the latest supplied coordinate list did not include a visible Tisu slot. It should be calibrated separately if it is currently visible/unlocked in the user's game layout.

## Validation checklist

1. Run shelf detection and confirm diagnostics show `E-Wallet`, `2 GB`, and `5 GB` when those labels are visible.
2. Confirm `Gula` is found. If not, inspect the left-shelf crop and raw OCR tokens; this patch intentionally does not invent its coordinate.
3. Confirm all 16 left-shelf and 16 right-shelf catalogue products remain detected.
4. Check that 4x pulse OCR does not create duplicate points or merge adjacent counter products.
5. Compare scan duration with v0.3.1.1; the counter crop has more pixels, but the main shelf passes are unchanged.
6. Verify each green marker visually. A text-match score is not coordinate accuracy.

## Safety boundary

This remains preview-only detection. No mouse clicks or order execution are added.
