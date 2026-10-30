# RabbitHole Product Context

<!-- impeccable:product-schema 1 -->

This is the durable product brief for future design work. [AGENTS.md](AGENTS.md) remains the repository-wide authority for product, privacy, security, and engineering constraints. This brief records owner-provided requirements and existing repository behavior; it is not a new feature acceptance report.

## Platform

web

## Users

- **Public visitors** browse a published collection of real articles, research, video, audio, and music; follow topics and related discoveries; and open original sources. They do not need or receive accounts.
- **One administrator** curates that collection and uses a private workspace to discover, save, read, annotate, explore branching trails, read personal books, and optionally revisit material through practice.
- **Both audiences have equal priority**, confirmed by the owner on 30 October 2026. Evaluate changes against both journeys while preserving their separate permissions and data boundaries.
- Visitors can also use **Blackjack Lab**, a separate public learning destination with lessons, strategy practice, and coached play using virtual chips.

## Product Purpose

Replace low-value doomscrolling with enjoyable, intentional discovery of human-created material. The north star is **A on the surface, B quietly underneath**: an inviting, surprising discovery experience first; cumulative understanding, connections, recall, and learning trails emerging over time.

Success means finding something worth exploring, following a useful connection, and being able to return later. Reading must not feel like compulsory coursework. More time spent, streaks, and apparent mastery from mere consumption are not success criteria.

## Positioning

Rabbit holes are a persistent interaction model: start with a real item, follow multiple related paths, branch, backtrack, move sideways, and resume later. They are not just a static recommendation list.

The public catalog and private learning workspace serve the same curiosity without sharing personal records. Discovery is grounded in imported sources and the owner's choices, works without AI, and supports both sustained interests and passing curiosity. Unusual subjects are legitimate interests; comparisons and counterarguments are available when requested rather than imposed as corrective framing.

## Operating Context

- Public routes are `/` for Catalog, `/search`, and `/items/{id}` for source previews and related discoveries. Catalog supports topic browsing; Search owns queries and advanced filters. Public headlines currently open the original source in a new tab; **Go down the rabbit hole** opens the RabbitHole preview.
- The private workspace is under `/admin`, with sign-in at `/admin/login`. Its core navigation is **Discover**, **Rabbit Holes**, **Library**, **Practice**, **Search**, and **Settings**. Blackjack Lab is also linked from both navigation shells.
- Private Discover offers a finite, configurable daily selection, topic pivots, save/read/dismiss actions, source links, **Why this?**, surprise, and trail creation. Browsing a topic must not silently change lasting interests or replace the daily selection.
- Readers encounter source material first, then optional notes, exploration, and details. Personal PDF/EPUB reading supports actual sections, progress, and original-file fallback. Saved items, notes, progress, and trails survive refreshes and restarts.
- Practice is optional and starts off. It uses encountered or chosen material, source passages, responses, and self-evaluation; it does not grant mastery for opening or finishing an item.
- `/blackjack` works independently of accounts, the catalog API, AI, and the database. Its Learn, Practice, Play, and reference views share one documented Atlantic City rules profile. Lesson progress and coaching preferences use browser storage; hands, virtual chips, and practice counts are session state. See [Blackjack Lab](docs/blackjack-lab.md) for current rules and limits.
- This is an existing Next.js web application with an ASP.NET Core API, PostgreSQL, and a Caddy gateway. Development uses `./scripts/dev.ps1 start` and `http://127.0.0.1:47832`. The preserved personal production instance and hosted deployment are separate; follow [environment operations](docs/environments.md) and [public hosting](docs/public-hosting.md) before deployment.

## Capabilities and Constraints

- **Real material only.** External items must originate from actual feed/API responses, supplied URLs, or user files. Preserve canonical links, provider identity, available creator information, and nullable source-reported dates. Missing dates are unknown, not today's date. Test fixtures never populate the real feed.
- **Publication is explicit and independent of personal state.** Eligible external imports default public, and the administrator controls their visibility. Saving, reading, dismissing, or disabling a source does not determine publication. Uploaded books, document-derived records, private or credential-bearing URLs, notes, history, trails, personal practice, AI output, and provider settings remain private.
- **Public rendering has its own contracts.** Expose only eligible metadata and bounded source excerpts. Apply visibility before search, counts, facets, pagination, and related ranking. Public requests do not forward administrator credentials, record personal state, run ingestion, or invoke AI.
- **Provenance is part of the experience.** Keep source assertions and original text distinguishable from AI synthesis. Connections and recommendations refer to actual records; a missing source match can become a search suggestion, never an invented link.
- **Learning states stay distinct.** Encountered, explicitly read, self-reported understood, and demonstrated recall are different concepts. Self-ratings record the user's assessment, not independently verified recall. A single exploration does not establish a durable interest.
- **AI is optional.** Deterministic search, ranking, reading, saving, document navigation, and trails remain useful without a model or key. Enabled AI is a bounded, server-side enrichment layer with grounded, labeled output. Secrets must never reach public responses or frontend bundles.
- **Failures remain honest and recoverable.** Empty collections stay empty. Explain unavailable providers or embeds, rate limits, unknown dates, parsing failures, and absent AI configuration. A failing source must not break healthy sources or existing personal content.
- **Personal control and respectful ingestion.** Starter topics are editable, and source subscriptions are deliberate. Preserve copyright and access restrictions; do not bypass paywalls or DRM, assume transcript access, or silently load third-party media. No analytics, ads, billing, public social network, or multi-tenant accounts.
- **Preserve user data.** Refreshes, deduplication, improvements, and migrations must retain publication choices and personal work. A rendered control is not complete until its behavior, persistence, and failure states work through the real application.

## Brand Commitments

The name is **RabbitHole**. The existing invitation, **Start anywhere. See where curiosity leads.**, expresses the product's central metaphor. Copy should be clear, inviting, and candid about sources and limitations, following the repository's plain explanations rather than introducing unsupported claims.

Owner-defined interests include frontier AI, philosophy and consciousness, speculative history, parapsychology, science, psychology, anthropology, software engineering, music production and discovery, and unexpected connections beyond those subjects. These are editable interests, not an immutable editorial policy.

Preserve the owner's requirement for a beautiful, calm, fast experience with comfortable reading and dark/light support. Specific visual systems and surface concepts are outside this product brief.

## Evidence on Hand

- [README.md](README.md) documents current user journeys, startup, provider limitations, and recovery commands.
- [Architecture](docs/architecture.md), [ingestion and privacy](docs/ingestion-and-privacy.md), and [acceptance report](docs/acceptance-report.md) describe implemented boundaries and recorded verification. Historical test results are not new verification.
- [Curated source pack](config/curated-source-pack.json) and [curated reading pack](config/curated-reading-pack.json) contain actual source candidates and selected links. They are opt-in inputs, not proof that every source is currently available or subscribed.
- [Feed curation](docs/feed-curation-report.md), [provider verification](docs/provider-verification.md), and [recommendation quality](docs/recommendation-quality-report.md) contain dated evidence and limitations. Do not turn historical item counts into permanent product claims.
- Existing route code, the [public shell](apps/web/components/public-shell.tsx), [private shell](apps/web/components/app-shell.tsx), and [shared styles](apps/web/app/globals.css) establish an incumbent interface. This is an existing product, not a blank visual brief.
- Clearly marked test material lives under `tests/`, including PDF/EPUB fixtures. Private user books, notes, and activity are never public proof assets. No customer testimonials, adoption figures, or efficacy claims are established by this brief.

## Product Principles

1. **Curiosity leads.** Make discovery inviting and finite; deeper exploration and practice remain deliberate choices.
2. **Sources stay real.** Human-created material, attribution, and provenance come before generated enrichment or apparent completeness.
3. **Both audiences matter.** Serve public discovery and private learning equally while maintaining a firm privacy boundary.
4. **The user retains control.** Respect unconventional interests, temporary curiosity, publication choices, and voluntary comparisons without thought-policing or engagement pressure.
5. **Understanding accumulates quietly.** Preserve trails and personal work, distinguish learning evidence honestly, and keep core use available when providers fail.

## Accessibility & Inclusion

Support responsive desktop and mobile web use, keyboard-accessible navigation and controls, clear focus, and readable content in dark and light appearances. Dark is the current default; saved Light, Dark, and Follow system choices take precedence.

Respect reduced-motion preferences, including skipping Blackjack Lab's presentation delays. Communicate loading, errors, empty states, and disabled capabilities plainly. No specific accessibility certification or audited conformance level has been established by this initialization.
