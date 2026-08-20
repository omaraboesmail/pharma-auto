# Android Invoice Review

- Scope: the first Android invoice-review and Posting Line editor.
- Mode: Operate.
- Audience: a non-technical pharmacy operator working one-handed at a bright counter.
- Job: compare OCR evidence with confirmed values, correct four commercial values, and add, edit, split, or remove expiry rows where every expiry owns a quantity.
- Primary action: review invoice totals and save an immutable review revision; never imply a Genius Commit.
- Proof: persistent OCR source values, explicit confirmed fields, synthetic line identity, and live EGP calculations.
- Constraints: preserve invoice order; use decimal strings; support Arabic/English and RTL/LTR; 48 dp targets; `minSdk 28`; Material 3; no policy cards or implementation jargon.

## Approved Direction

- Composition: Simple previous/next, approved from `.impeccable/mocks/hybrid-progressive.webp`.
- Memorable moment: the current item reads as one linear task—source-versus-confirmed values, then quantity/expiry rows, then one expandable totals/save footer.
- Color strategy: restrained light surfaces with forest-green primary roles, pale-green confirmed values, amber reserved for uncertain OCR, and semantic error red only for destructive confirmation.
- Component grammar: Material 3 top app bar, plain text navigation, thin dividers, 8–12 dp shape language, outlined fields, stacked expiry rows, and a bottom anchored expandable surface.

## Fidelity Inventory

| Region | Commitment | Medium |
|---|---|---|
| App and review header | Compact Material top bar, progress and current context | Compose Material 3 |
| Line navigation | Previous/current/next with system Back compatibility | Semantic Compose buttons |
| Evidence editor | Four rows, OCR source left and confirmed editable value right | Compose layout and outlined fields |
| Expiry editor | Stacked rows; quantity and date per row; labeled Split/Remove/Add actions | Compose fields, DatePickerDialog, state list |
| Totals and save | Anchored expandable surface combining invoice totals and save | Compose animated visibility and primary button |
| Icons | Familiar Material-style vector paths with text labels where meaning matters | Android vector/Compose paths |
| Imagery | None required; OCR evidence opens as a semantic placeholder state | Accepted omission for initialization slice |

## Unresolved

- Real OCR image cropping and server-backed persistence replace the synthetic initialization data in a later vertical slice.
