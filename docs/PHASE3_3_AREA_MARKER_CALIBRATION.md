# WABA Desktop v0.3.3 — Area-Specific Marker Calibration

## User-reported alignment

The v0.3.2 screenshot showed:
- `LEFT_SHELF`: marker positions already aligned.
- `RIGHT_SHELF`: markers needed to move right.
- `PULSE_COUNTER`: markers needed to move down.

## Implementation

`StableCatalogManager` now applies area-specific offsets to the reference coordinates while
building the returned `StableCatalogSnapshot`.

| Area | Offset | Affected slots |
|---|---:|---:|
| LEFT_SHELF | X 0, Y 0 | 16 |
| RIGHT_SHELF | X +24, Y 0 | 16 |
| PULSE_COUNTER | X 0, Y +18 | 5 |

Offsets are expressed in the reference frame (1831×1028). The adjusted reference coordinates
are then scaled to the current captured frame dimensions.

## Why offsets are not persisted into the baseline

The saved stable catalog remains the original coordinate baseline. Applying offsets only when
producing the snapshot prevents the correction from being duplicated in an existing local
catalog file and keeps OCR recalibration observations separate from the manual area-level
alignment correction.

## Verification checklist

- [ ] Confirm all 16 left-shelf markers remain unchanged.
- [ ] Confirm all 16 right-shelf markers shift right.
- [ ] Confirm all 5 counter markers shift down.
- [ ] Confirm marker movement scales correctly when the client frame is resized.
- [ ] Confirm OCR coverage remains independent from marker offsets.
- [ ] Do not use marker coordinates for automated input until visual validation is complete.

## Safety

This change affects returned marker coordinates and preview overlays. It does not introduce
automatic mouse clicks or background order execution.
