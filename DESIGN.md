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
    fontFamily: "Arial, Helvetica, sans-serif"
    fontSize: "24px"
    fontWeight: 600
    lineHeight: 1.25
    letterSpacing: "-0.025em"
  public-story-title:
    fontFamily: "Arial, Helvetica, sans-serif"
    fontSize: "16px"
    fontWeight: 600
    lineHeight: 1.45
    letterSpacing: "-0.01em"
  public-item-title:
    fontFamily: "Arial, Helvetica, sans-serif"
    fontSize: "30px"
    fontWeight: 600
    lineHeight: 1.25
    letterSpacing: "-0.025em"
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

This records the implemented system, including the owner's 30 October 2026 request for a more functional, Hacker News-like home page. [PRODUCT.md](PRODUCT.md) and [AGENTS.md](AGENTS.md) retain product and engineering authority.

The public catalog uses a compact masthead, topic navigation and numbered source rows. The private workspace retains its sidebar and serif discovery headings. Both share the warm palette, serif reading text and sans-serif controls. Public styles live in `apps/web/components/public-shell.css`; shared and private styles remain in `apps/web/app/globals.css`. Blackjack Lab adds scoped styles in `apps/web/app/(public)/blackjack/blackjack.css`.

**Key Characteristics:**

- Compact public masthead and private workspace sidebar with separate route and data boundaries.
- Compact numbered discovery rows and source metadata.
- Serif reading typography and sans-serif interface controls.
- Dark, Light, and Follow system appearances.

## Colors

The frontmatter records the active dark-theme values under the existing CSS variable names and light-theme counterparts with a `light-` prefix. Components use CSS variables so appearance follows the active theme; do not hard-code the dark values into shared components.

`bg`, `paper`, and `paper2` distinguish the page, surfaces, and secondary surfaces. `ink` and `muted` distinguish primary text and metadata. `line` supplies dividers. `accent` and `accentsoft` identify links, active navigation, and selected controls. `lilac` and `lilacink` support callouts and text selection.

Dark is the initial appearance. Saved Light, Dark, or Follow system preferences take precedence. Blackjack Lab owns additional local felt and green tokens; these do not replace the shared palette.

## Typography

The public catalog uses Arial/Helvetica headings: 24px for the page title (22px on phones), 16px semibold for source headlines, 12px excerpts and 11px metadata. The brand and reader retain Georgia. Private discovery headlines remain 18px Georgia; reader text is 18px with 1.8 line height and becomes 17px at the mobile breakpoint. The system sans choice is deliberate for the owner's compact link-index brief; no font download is required.

These values describe the incumbent implementation, including its existing tight heading tracking. They are not a claim that every current style has passed a design or accessibility audit.

## Layout

The public masthead and content align in a 1680px maximum-width container with 32px horizontal padding, widened at the owner's request. A 72px masthead replaces the public sidebar and duplicate header. On screens at or below 720px, navigation wraps beneath the brand, horizontal padding becomes 20px, and the topic bar scrolls horizontally. There is no fixed public bottom navigation. The private shell retains its 238px sidebar (200px below 1040px), 1380px main width and mobile bottom navigation.

Public story rows place the headline and source domain first, a bounded source excerpt next, then attribution, date, type and exploration links in a wrapping footer. Metadata and actions share a line when space permits and stack on phones. The list uses divider lines, with no row background on hover or focus; individual link hover and keyboard focus remain visible. Public empty states use the same flat structure; service failures offer a reload link. Topic and page changes are ordinary links with server-rendered results.

Public item details align to this wider grid. Their title is 30px sans-serif (24px on phones), followed by source metadata and plain topic links. The source excerpt is 18px Georgia at 1.7 line height (17px on phones), constrained to 75ch and aligned to the left. One underlined original-source action and a plain preview notice replace the duplicate actions and notice panel. Related discoveries span the container beneath a single divider. Media still requires consent before loading. These styles are scoped to `.public-item-page`; private readers retain their existing typography and controls.

Discovery rows use a number column and a flexible content column. Wider private discovery views can show saved trails alongside the list. The reader has an 820px maximum width; notes and exploration follow source content. Preserve real content and responsive behavior when previewing variants.

## Elevation & Depth

Borders and surface colors establish most hierarchy. Panels use `--shadow`: dark `0 12px 36px rgba(0,0,0,.16)`, light `0 12px 36px rgba(43,36,27,.045)`. Popovers and reader dialogs have their own overlay shadows. These are existing values, not an expanded elevation scale.

## Shapes

Shared controls have 7px corners, private navigation items 8px, panels 11px, and pills 30px. Public pagination and search panels have 4px corners; the public arrow mark has 3px corners. The private brand mark remains circular. Dividers and most surface borders are 1px. Keep the separate Blackjack Lab card and table styling scoped to that feature.

## Components

- **Buttons:** primary uses ink on paper contrast reversed; secondary uses paper with a line border; accent uses the accent-soft surface. Hover uses the accent. Disabled controls reduce opacity and use a not-allowed cursor.
- **Inputs:** paper background, ink text, line border, 7px corners, and 11px by 12px padding. Focus changes the border to accent.
- **Navigation:** public links use text with an accent underline and semibold current-page state; private links retain their accentsoft active surface. The public shell provides a keyboard skip link. Public and private navigation retain their distinct destinations.
- **Public metadata:** `--catalog-muted` mixes 85% muted with ink for stronger light-theme contrast. Selected topics and exploration links use the existing accent.
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
