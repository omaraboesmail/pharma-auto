---
name: Pharma Auto
description: A calm Material 3 review system for evidence-led pharmacy invoice work.
colors:
  primary: "#075D35"
  primary-deep: "#00391F"
  primary-container: "#C6F0D5"
  confirmed: "#E8F5EB"
  ink: "#171D19"
  canvas: "#F9FBF7"
  surface: "#FFFFFF"
  cool-surface: "#F0F3EF"
  evidence-amber: "#8A5100"
  evidence-amber-container: "#FFDDB5"
  error: "#BA1A1A"
  outline: "#707972"
  outline-soft: "#C0C9C1"
  dark-primary: "#88D5A4"
  dark-canvas: "#101411"
  dark-surface: "#252A26"
  dark-ink: "#E0E4DE"
typography:
  title:
    fontFamily: "Roboto, system-ui, sans-serif"
    fontSize: "22px"
    fontWeight: 600
    lineHeight: 1.27
  body:
    fontFamily: "Roboto, system-ui, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.43
  label:
    fontFamily: "Roboto, system-ui, sans-serif"
    fontSize: "14px"
    fontWeight: 500
    lineHeight: 1.43
rounded:
  small: "8px"
  medium: "12px"
  large: "16px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "20px"
  xxl: "24px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.surface}"
    typography: "{typography.label}"
    rounded: "{rounded.small}"
    padding: "16px 24px"
    height: "56px"
  button-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.primary}"
    typography: "{typography.label}"
    rounded: "{rounded.small}"
    padding: "12px 16px"
    height: "52px"
  field-confirmed:
    backgroundColor: "{colors.confirmed}"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    rounded: "{rounded.small}"
    padding: "10px 14px"
    height: "64px"
  expiry-container:
    backgroundColor: "{colors.cool-surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.medium}"
    padding: "14px"
  totals-footer:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    padding: "12px 16px"
---

# Design System: Pharma Auto

## Overview

**Creative North Star: "The Calm Review Counter"**

Pharma Auto should feel like a clear, well-lit pharmacy counter where one consequential task is handled at a time. The interface uses familiar Material 3 behavior, restrained color, explicit labels, and progressive disclosure so a nontechnical operator can compare evidence, correct a value, and recover from an error without learning system terminology.

Evidence and confirmed data stay visually distinct, while consequential states remain truthful. The current Android surface is a non-persistent prototype check: its design must never imply that data was saved, sent, or committed to Genius when that action has not occurred.

**Key Characteristics:**

- Calm, linear review instead of dense all-lines editing.
- OCR evidence remains visible and unchanged beside confirmed values.
- Forest green carries primary action and progress; amber is reserved for uncertain evidence.
- English and Arabic receive equal hierarchy, readable wrapping, and native RTL/LTR behavior.
- Large controls and plain-language recovery support one-handed counter work.

## Colors

The palette is a cool, restrained Material system: forest green establishes trust, pale green separates confirmed values, and neutral surfaces keep financial evidence legible.

### Primary

- **Counter Forest** (`primary`): progress, focus, and the single primary action.
- **Deep Forest** (`primary-deep`): high-contrast content on pale green containers.
- **Fresh Confirmation** (`primary-container`): selected or positive context that is not an action.

### Secondary

- **Confirmed Wash** (`confirmed`): the editable confirmed side of evidence comparisons.

### Tertiary

- **Evidence Amber** (`evidence-amber` and `evidence-amber-container`): uncertain OCR evidence only.
- **Recovery Red** (`error`): invalid input and destructive removal only.

### Neutral

- **Counter Ink** (`ink`): primary light-theme text.
- **Quiet Canvas** (`canvas`): app background.
- **Clean Surface** (`surface`): top bars, fields, and the totals surface.
- **Cool Work Surface** (`cool-surface`): expiry rows and grouped work areas.
- **Measured Outline** (`outline` and `outline-soft`): field borders and dividers.
- **Night Counter** (`dark-canvas`, `dark-surface`, `dark-ink`, and `dark-primary`): semantic dark-theme equivalents; roles do not change with theme.

**The One Amber Meaning Rule.** Amber communicates uncertain source evidence and must not become a decorative accent.

## Typography

**Display Font:** Android system sans (Roboto where available)
**Body Font:** Android system sans (Roboto where available)

**Character:** Neutral, familiar, and operational. Pharma Auto uses the platform Material 3 type system without a custom display face; hierarchy comes from weight, spacing, and semantic placement rather than branding flourishes.

### Hierarchy

- **Title** (semibold, 22sp-equivalent): major section headings and definitive totals.
- **Body** (regular, 14sp-equivalent): evidence, values, explanations, and recovery copy.
- **Label** (medium, 14sp-equivalent): buttons, field labels, progress, and concise metadata.

**The Plain Language Rule.** Labels name the operator's task or data directly; implementation terms and policy prose stay out of the working screen.

## Layout

The primary grammar is one vertical review path: review context, previous/current/next navigation, OCR evidence, four commercial comparisons, expiry-owned quantities and dates, then one expandable totals/finish surface. Content is centered and capped at 840dp on wider displays while the bottom action stays anchored inside safe drawing, IME, and navigation-bar insets.

Use the implemented 4/8/12/16/20/24dp rhythm. Commercial source and confirmed fields stack below 400dp; expiry controls stack below 420dp. Every interactive target is at least 48dp, and the primary finish action is 56dp high. Arabic layouts mirror through platform direction rather than persisted BiDi marks.

**The One Linear Task Rule.** Progressive disclosure may shorten a screen, but it must not hide which invoice line or expiry row an edit belongs to.

## Elevation & Depth

The system is flat by default. White, cool-gray, pale-green, and amber tonal surfaces create most separation; thin outline dividers preserve scanning without turning the page into a grid. The anchored totals footer is the single lifted work surface, using 8dp shadow elevation and 3dp tonal elevation so totals and completion remain available above scrolling content.

**The Flat By Default Rule.** Elevation marks persistent action hierarchy, not decoration; ordinary evidence and expiry groups use tonal layering instead of shadows.

## Shapes

Small controls and fields use gently curved 8dp corners, grouped work surfaces use 12dp corners, and large containers may use 16dp corners. Circular shapes are reserved for compact status imagery. Avoid both sharp spreadsheet cells and oversized pill silhouettes.

## Components

### Buttons

- **Primary:** 56dp-high forest action with an 8dp radius, white label, and a clear forward affordance.
- **Secondary:** outlined or text action with an explicit label; destructive removal uses semantic red and never relies on an icon alone.
- **Focus / disabled:** use Material state behavior and preserve readable contrast; disabled previous/next actions remain visibly unavailable.

### Cards / Containers

- **Evidence comparison:** source stays on a neutral surface; confirmed input stays on pale green. Both belong to one row rather than separate floating cards.
- **Expiry row:** a 12dp cool-surface group owns its quantity, date, and one clear Split or Remove action.
- **Totals footer:** an anchored expandable surface combines truthful totals, finish review, and the visible prototype boundary.

### Inputs / Fields

- **Style:** 64dp minimum height, 8dp radius, explicit label, numeric keyboard where appropriate, and visible EGP/percent/box context.
- **Error:** invalid fields use semantic red and specific recovery copy. Totals become unavailable when required data is invalid; never calculate a plausible number from substituted zeroes.
- **Accessibility:** repeated controls include the expiry-row number in their accessible name.

### Navigation

Previous/current/next keeps one line in focus. Advancing validates and completes the current line; a blocking finish action returns the operator to the first invalid or unreviewed line and scrolls to the relevant section.

**The Honest State Rule.** A control may say save or show a definitive total only when the product has actually performed that operation or completed that calculation.

## Do's and Don'ts

### Do:

- **Do** preserve raw OCR evidence while showing confirmed values separately.
- **Do** use explicit labels, row ownership, 48dp targets, and specific recovery messages.
- **Do** keep financial totals unavailable until every required value is valid.
- **Do** mirror layouts for Arabic while isolating mixed-direction display text only in the UI.
- **Do** reserve the anchored footer for totals and the single completion action.

### Don't:

- **Don't** imply persistence, Connector submission, or a Genius commit before it occurs.
- **Don't** use amber, red, shadows, cards, or pills as decoration.
- **Don't** hide expiry quantity ownership or place multiple unlabeled icon-only actions on a row.
- **Don't** reprice old stock or surface policy prose as if it were an editable screen control.
- **Don't** replace the linear review flow with a dense spreadsheet-style editor.
