# Phase 4.1 — Guided Order Queue UI

## User-facing flow

Section 06 is divided into preparation, plan creation/review, and execution. It explains that the catalogue is prepared in section 05 and the order panel is read from a fresh frame. The main button captures a new frame if live preview is not active, runs order OCR, and then calls the existing plan builder.

## Preconditions

- A game window must be selected.
- The current session must have a 37-slot catalogue scan.
- The catalogue must be locked.
- The user must explicitly confirm visual marker inspection.
- The captured frame layout must match the catalogue baseline.
- OCR must identify all order products and quantities without ambiguity.

## Execution gating

The plan builder remains separate from the click executor. The plan is displayed before the execution button becomes enabled. The user must approve the existing confirmation dialog. A plan becomes stale after 20 seconds or when a new OCR/catalogue scan invalidates it.

## Visual improvements

- Dark, readable disabled button states.
- Dark Google Play Games selector with light text and styled list items.
- Clear section headings and plain-language instructions.
- Separate plan status and plan content areas.
- Explicit warning that the application does not yet verify that the game accepted each click.

## Safety boundary

This phase remains supervised. It does not automate customer dialogs, tips, or minigames, and it does not perform closed-loop item verification after each click.
