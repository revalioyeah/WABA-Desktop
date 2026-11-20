# WABA Desktop v0.4.0 — Phase 4.0 Smart Order Queue

## Purpose

Add a reviewable, confirm-gated multi-item order queue using the stable 37-slot catalogue established in v0.3.3.

## New features

- Order execution planner maps recognized order rows to exactly one stable catalogue slot.
- Remaining quantities are calculated from `completed/requested`.
- A plan is blocked when a product is unknown, quantity is unreadable, product rows are duplicated, a coordinate is missing/out of bounds, the catalogue is unlocked/incomplete, the frame layout differs, or the click count exceeds the safety limit.
- UI shows each planned product, quantity, coordinate, and area before execution.
- User must confirm that all 37 catalogue markers were visually checked.
- User must explicitly confirm each real mouse-click batch.
- Queue sends one click per remaining unit, with a 105 ms interval.
- The game window is focused once per batch rather than waiting 120 ms before every click.
- A Stop button cancels the remaining queue.
- Queue stops if the selected game window loses foreground focus.
- Queue plans are invalidated after new order OCR or a new catalogue scan.
- A plan expires when the latest order OCR is more than 8 seconds old.

## Preserved behavior

- Stable Catalog 37-slot baseline and v0.3.3 area offsets remain unchanged.
- Order OCR and shelf OCR recognition algorithms are unchanged.
- Existing manual one-click test remains available.
- No automatic queue starts merely because OCR detects an order.

## Important limitations

- Windows accepting a mouse event does not prove that the game registered the item.
- The queue does not yet re-scan and verify quantities after each click.
- Tip notifications, customer dialogs, and minigames are not automatically handled.
- The user must keep the game visible and unobstructed, review the plan, confirm execution, and verify the result.

## Validation

This source patch was inspected and structurally updated. A local .NET publish could not be run in the authoring environment because the .NET SDK is not installed. GitHub Actions must complete a Windows x64 publish before this build is treated as compiled.
