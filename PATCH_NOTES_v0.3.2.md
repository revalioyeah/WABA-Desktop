# Patch v0.3.2 — Stable Catalog Engine

This patch addresses catalogue flicker caused by replacing the full visible product list with the latest OCR result.

- Adds `StableCatalogManager` with a 37-product user-reported reference catalogue.
- Preserves products that are temporarily missed by OCR.
- Separates latest OCR observations from retained catalogue entries.
- Adds lock and explicit recalibration controls.
- Uses 2-of-3 coordinate consensus before accepting recalibration updates.
- Persists the catalogue state locally.
- Does not change the OCR detector's recognition pipeline or enable automatic gestures.

See `docs/PHASE3_2_STABLE_CATALOG_ENGINE.md` for details.
