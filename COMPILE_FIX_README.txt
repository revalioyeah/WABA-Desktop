WABA Desktop v0.3.1.3 compile fix

Replace the repository file at:
src/WABA.Recognition/Services/ShelfItemDetector.cs

The local helper TryCreateCandidate must accept `double expectedX`, because the inferred slot positions are calculated using fractional spacing. The value is rounded and converted to int only when constructing ShelfItemDetection.

This patch does not change OCR, shelf regions, catalog entries, or execution behavior.
