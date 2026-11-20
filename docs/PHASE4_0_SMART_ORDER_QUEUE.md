# Phase 4.0 — Smart Order Queue & Confirm-Gated Execution

## Objective

Build a deterministic, reviewable path from the latest order OCR to a bounded sequence of real mouse clicks, using the v0.3.3 stable 37-slot catalogue.

## Pipeline

`Latest OCR` → `OrderExecutionPlanner` → `Validated OrderExecutionPlan` → `User confirmation` → `Focus target once` → `Fast click sequence`

## Plan validation

The planner blocks execution when any of these conditions is true:

- Stable catalogue is not locked.
- Catalogue does not contain exactly 37 entries.
- Captured frame aspect ratio does not match the catalogue baseline.
- A product label is unknown.
- A completed/requested quantity is unreadable or invalid.
- A known product appears more than once in the parsed order rows.
- A requested product has no unique catalogue coordinate.
- A coordinate is outside the captured game client area.
- One product quantity exceeds 10.
- Total click count exceeds 20.
- No remaining quantity is present.

The app additionally requires:
- The user to check the visual marker confirmation after reviewing the catalogue preview.
- The catalogue scan to be less than 10 minutes old.
- The order OCR to be less than 8 seconds old.
- The latest order OCR and catalogue scan to use the same captured client dimensions.

## Execution behavior

- The user sees product, remaining quantity, coordinate, and shelf area before execution.
- A confirmation dialog lists the exact planned products and total number of clicks.
- The game window is brought to the foreground once; the queue stops if the foreground window changes.
- Each remaining unit receives one left click at the stable catalogue coordinate.
- Initial inter-click delay: 105 ms.
- The user can stop the remaining queue using the Stop button.
- New OCR or catalogue scans invalidate the prior plan.
- After a batch, the user must inspect the game order board and bag.

## Known limitations

- A successful `SendInput` only means Windows accepted the mouse event; it does not prove the game registered the item.
- The queue does not yet close the loop by OCR-verifying the quantity after each click.
- Tip notices, customer dialogs, and minigames are not automatically classified or dismissed.
- No automatic stock validation or retry policy is included.
- The queue is confirm-gated and should not be left unattended.

## Test progression

1. Build a plan for one product with quantity 1.
2. Execute and verify the bag and order counter.
3. Test two distinct products.
4. Test one product with quantity greater than 1.
5. Test 4–5 item orders.
6. Only after the above pass, test 7–8 item orders.
7. Record actual click-to-registration behavior and adjust the inter-click interval only from observed results.
