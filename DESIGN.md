---
name: RabbitHole
description: Existing public catalog and private learning workspace.
colors:
  bg: "#191918"
  paper: "#242320"
  paper2: "#2d2b28"
  ink: "#f1ede5"
  muted: "#aba69e"
  line: "#3b3833"
  accent: "#dd947c"
  accentsoft: "#3c2b28"
  lilac: "#3a3343"
  lilacink: "#c9b8dc"
  light-bg: "#f7f5f0"
  light-paper: "#fffefa"
  light-paper2: "#f1eee8"
  light-ink: "#292825"
  light-muted: "#77726c"
  light-line: "#e5e0d8"
  light-accent: "#aa4f36"
  light-accentsoft: "#f4e8e1"
  light-lilac: "#e7e1ee"
  light-lilacink: "#695775"
typography:
  body:
    fontFamily: "Arial, Helvetica, sans-serif"
    fontSize: "14px"
  catalog-heading:
    fontFamily: "Georgia, 'Times New Roman', serif"
    fontSize: "clamp(30px, 3vw, 38px)"
    fontWeight: 400
    lineHeight: 1.05
    letterSpacing: "-0.045em"
  story-title:
    fontFamily: "Georgia, 'Times New Roman', serif"
    fontSize: "18px"
    fontWeight: 400
    lineHeight: 1.35
    letterSpacing: "-0.015em"
  reader-body:
    fontFamily: "Georgia, serif"
    fontSize: "18px"
    lineHeight: 1.8
    letterSpacing: "0.005em"
  button:
    fontFamily: "Arial, Helvetica, sans-serif"
    fontSize: "12px"
    fontWeight: 600
rounded:
  control: "7px"
  navigation: "8px"
  panel: "11px"
  pill: "30px"
spacing:
  action-gap: "8px"
  panel-padding: "23px"
components:
  button-primary:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.paper}"
    typography: "{typography.button}"
    rounded: "{rounded.control}"
    padding: "10px 15px"
  button-secondary:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    typography: "{typography.button}"
    rounded: "{rounded.control}"
    padding: "10px 15px"
  button-accent:
    backgroundColor: "{colors.accentsoft}"
    textColor: "{colors.accent}"
    typography: "{typography.button}"
    rounded: "{rounded.control}"
    padding: "10px 15px"
  panel:
    backgroundColor: "{colors.paper}"
    rounded: "{rounded.panel}"
    padding: "23px"
---

# Design System: RabbitHole

## Overview

This is a factual capture of the existing implementation for Impeccable live mode, recorded on 30 October 2026. It does not establish a new visual direction or introduce qualitative brand decisions. [PRODUCT.md](PRODUCT.md) and [AGENTS.md](AGENTS.md) retain product and engineering authority.

The catalog and private workspace share a sidebar, compact source lists, serif headings and reading text, and sans-serif controls. Source files are `apps/web/app/globals.css`, `apps/web/components/public-shell.tsx`, and `apps/web/components/app-shell.tsx`. Blackjack Lab adds scoped styles in `apps/web/app/(public)/blackjack/blackjack.css`.

**Key Characteristics:**

- Shared public/private navigation structure with separate route and data boundaries.
- Compact numbered discovery rows and source metadata.
- Serif reading typography and sans-serif interface controls.
- Dark, Light, and Follow system appearances.

## Colors

The frontmatter records the active dark-theme values under the existing CSS variable names and light-theme counterparts with a `light-` prefix. Components use CSS variables so appearance follows the active theme; do not hard-code the dark values into shared components.

`bg`, `paper`, and `paper2` distinguish the page, surfaces, and secondary surfaces. `ink` and `muted` distinguish primary text and metadata. `line` supplies dividers. `accent` and `accentsoft` identify links, active navigation, and selected controls. `lilac` and `lilacink` support callouts and text selection.

Dark is the initial appearance. Saved Light, Dark, or Follow system preferences take precedence. Blackjack Lab owns additional local felt and green tokens; these do not replace the shared palette.

## Typography

The observed interface uses Arial/Helvetica for controls and metadata, and Georgia with serif fallbacks for headings and source text. The compact catalog heading overrides the larger general page-heading scale. Story headings are 18px; reader text is 18px with 1.8 line height and becomes 17px at the mobile breakpoint.

These values describe the incumbent implementation, including its existing tight heading tracking. They are not a claim that every current style has passed a design or accessibility audit.

## Layout

The desktop shell has a 238px sidebar and a flexible main column; the sidebar becomes 200px below 1040px. The main content has a 1380px maximum width and 5% horizontal padding. Below 720px, the sidebar is replaced by bottom navigation and forms become one column.

Discovery rows use a number column and a flexible content column. Wider private discovery views can show saved trails alongside the list. The reader has an 820px maximum width; notes and exploration follow source content. Preserve real content and responsive behavior when previewing variants.

## Elevation & Depth

Borders and surface colors establish most hierarchy. Panels use `--shadow`: dark `0 12px 36px rgba(0,0,0,.16)`, light `0 12px 36px rgba(43,36,27,.045)`. Popovers and reader dialogs have their own overlay shadows. These are existing values, not an expanded elevation scale.

## Shapes

Controls have 7px corners, navigation items 8px, panels 11px, and pills 30px. Dividers and most surface borders are 1px. The existing brand mark is circular. Keep the separate Blackjack Lab card and table styling scoped to that feature.

## Components

- **Buttons:** primary uses ink on paper contrast reversed; secondary uses paper with a line border; accent uses the accent-soft surface. Hover uses the accent. Disabled controls reduce opacity and use a not-allowed cursor.
- **Inputs:** paper background, ink text, line border, 7px corners, and 11px by 12px padding. Focus changes the border to accent.
- **Navigation:** muted labels; hover uses paper2; active links use accentsoft and accent. Public and private navigation retain their distinct destinations.
- **Topic pills:** paper background and line border; active state uses accentsoft and accent.
- **Panels:** paper, line border, 11px corners, and the theme shadow. Numbered discovery rows remain compact list entries.
- **Reader:** source text precedes Notes, Explore, and Details. Contents navigation uses a dialog with its own scrollable layout.
- **Focus:** the shared visible-focus rule is a 2px accent outline with a 3px offset. Preserve keyboard access and meaningful labels. Blackjack Lab skips animation and presentation delays for reduced motion.

## Do's and Don'ts

- Do reuse the existing CSS variables and shared primitives.
- Do preserve public/private boundaries, actual source content, and functional controls.
- Do check dark and light appearance and desktop/mobile layouts for intended UI changes.
- Do treat this record as observed implementation, not a completed accessibility audit.
- Don't replace the established interface identity during a scoped refinement.
- Don't add invented articles, metadata, claims, or source links to make a preview look complete.
- Don't ship Impeccable live instrumentation in production builds.
