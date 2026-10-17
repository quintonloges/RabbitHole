# RABBITHOLE — ASTRA FULL ONE-SHOT IMPLEMENTATION MISSION

You are the principal engineer, product designer, researcher, and QA lead for **RabbitHole**. Work directly in the current repository. **Build the complete, functioning personal application in this run. Do not just write a plan, prototype, architecture document, or initial scaffold.** You have latitude to make reasonable implementation decisions and should proceed autonomously without routine questions. The owner is deliberately allocating substantial model/compute usage to this one-shot implementation.

**First actions:** Inspect the repository, read `AGENTS.md` in full, assess tools/runtimes available, and make a short internal execution plan. If this is a blank repository, create the whole app. If there is existing working code, preserve it and adapt. Treat `AGENTS.md` as persistent repository requirements and this prompt as the initial delivery contract.

**Most important product statement:**

> **RabbitHole is an exciting personalized discovery feed on the surface, with a cumulative, optional learning engine quietly operating underneath.** The interface should feel like discovering amazing things on the old internet, not enrolling in school or using a corporate research dashboard. The user follows curiosity down branching rabbit holes, while prior reading naturally informs future recommendations and optional recall.

The owner is technically sophisticated and will run this locally. The app is **private, single-user, self-hosted, deliberately opinionated, and not intended for public release or monetization**. The owner values exploration, intellectual autonomy, novel associations, honest provenance, strong engineering, and excellent UX.

---

## 0. Execution rules — read before coding

1. **Implement end-to-end, test, fix, and deliver.** Continue from design to schema to services to ingestion to UI to tests to documentation to local start verification. Don't stop to ask if you should proceed to a next phase. Commit coherent code if git is available, but do not assume remote hosting or push to remote.
2. **No mock product masquerading as real.** Never fabricate articles, paper titles, sources, dates, links, music, transcripts, citations, recommendation evidence, ingestion metrics, or API response success. A deterministic, visibly labeled test/demo dataset is fine for automated tests, but *not* as filler for the owner's real feed.
3. **No thought-policing.** Unconventional topics (UFOs, parapsychology, occult, alternative history, conspiracies, cryptids, etc.) are first-class interests. No automatic credibility badges, "misinformation warnings," fact-check overlays, condescending corrective language, suppression policies, or forced mainstream narratives. This is a personal curiosity engine. Still maintain factual honesty: precisely attribute claims and distinguish real sources from AI synthesis. Make debate/counterarguments an optional, explicitly requested tool.
4. **Real user outcomes matter more than sheer feature count.** Wire actual UI actions to persisted state and verified backend behavior. Avoid placeholder buttons, TODO views, no-op controls, fake progress, mock API payloads in production, and shells with hundreds of stub endpoints. If a hard external restriction exists, implement a well-labeled fallback and document the actual gap.
5. **Never require an AI API key to get started.** The app must ingest, render, search, bookmark, navigate rabbit holes, and provide basic recommendations without AI; AI augments those behaviors when configured.
6. **Be a careful steward of local data.** Never discard uploads or reset the database as a development convenience once data exists. Migrations must be forward-only and safe. By default expose only `127.0.0.1`.
7. **Use current official provider docs during implementation.** External APIs and quotas change; don't rely on an assumed endpoint or undocumented YouTube/SoundCloud scraper. The end of this prompt provides starting references.
8. **Finish honestly.** Run commands and tests, record results, resolve defects, and identify unavoidable limitations. No claiming tests passed unless you actually executed them.

**Interpretation of scope:** This is a full one-shot *working implementation*, not a request for a multi-release roadmap. Implement all core experiences and meaningful integrations in this execution. Choose robust simple mechanisms where complex AI solutions would take longer and be harder to validate. If the environment prevents a live provider check, complete everything testable with fixture responses and explicitly mark the live check unverified.

---

## 1. Product experience and navigation

Build a responsive, polished application with these primary destinations:

### Discover (`/` or `/discover`)

This is the home screen and the primary daily use case.

- A curated feed of **real** items: articles, devblogs, scholarly papers, uploaded book chapters, videos, podcasts/audio links, SoundCloud tracks, and fascinating older evergreen pieces.
- Beautiful editorial cards showing title, human creator/source, content type, available published date, description/excerpt, topic(s), estimated reading/listening/watch time when inferable, an optional thumbnail, and an obvious original-source link.
- Cards distinguish **source excerpt** from **AI context**. If the date is missing, show no date or "date unknown," not "posted today."
- Main actions: **Open/Read/Play**, **Save**, **Mark read**, **Not interested**, **More like this**, **Follow rabbit hole**, and a human-readable **Why this?** rationale. Implement real persistence and immediate optimistic feedback with rollback on failure.
- A **finite daily edition** (default about 25 items, configurable): after reaching the end, display a satisfying "You're caught up" state. Include an intentional **Keep exploring** action that requests more; never artificially cap access.
- Topic chips/pivots and optional filters for type, sources, new/saved/read, and recency. Respect the user's selected topics, including weird ones.
- **Surprise me** introduces serendipitous material that is legitimately present in the ingested catalog, not AI-invented media.
- Returning users should see meaningful continuity: "New since last visit," "Continue this rabbit hole," "Something related to a book chapter you read," "An adjacent interest," etc., without pressure or notifications.
- Empty/new installation: friendly onboarding explaining how to add topics, confirm suggested sources, import a book, and trigger refresh; show progress/errors truthfully.

### Item reader/detail (`/item/[id]`)

- Reading-focused layout with good typography, section headings, source/author/date/DOI where relevant, content type, original URL, saves, notes, and reading progress.
- Where a permitted RSS/feed supplies article content, render sanitized content. Where there is no licensed full text, present abstract/excerpt/metadata and a prominent **Open original** action. Do not bypass paywalls or silently scrape copyrighted full text.
- Papers: title, authors, venue/year, abstract (if present), DOI/arXiv/provider IDs, citation links when known, original/open-access URLs if returned, and related papers. Do not imply you've read a PDF unless it was actually retrieved or uploaded.
- Local books: comfortable chapter rendering, TOC navigation, saved progress, highlights or notes, an original PDF/EPUB viewing/download fallback, and relevant commentary links only if grounded in actual imported material or verified search results.
- Videos: source metadata and official embedded player; use privacy-conscious YouTube embed when feasible. Never require transcript access just to display a video.
- SoundCloud: official permitted track/playlist embeds using verified media URLs; no illicit downloads or misleading playback claims.
- A grounded "Why this relates to what you've read" panel, with links back to actual items. If no specific relationship exists, do not invent one.
- On each item: a prominent **Go deeper / Follow the rabbit hole** entry point. Notes and highlights should persist.

### Rabbit Holes (`/holes`, `/holes/[id]`)

**This is a defining differentiator; implement it as a working persistent exploration system, not a decorative UI.**

- Any item can start a rabbit hole. A rabbit hole has a starting item/question, a title, creation/update times, branches, nodes, and most recently visited position.
- From a node, recommend multiple **real** related items spanning potentially different disciplines. Explain the connection briefly and ground it in shared topic, creator, cited paper, concept relationship, or source snippets. If AI is enabled, it may explain a relationship using the actual retrieved metadata/text; without AI, deterministic relationships must still function.
- A node may have several child branches. Choosing one adds/navigates along that path without deleting alternative branches. Users can go back, switch siblings, retrace breadcrumbs, jump to a past node, and return to the initial item.
- Show the growing branch structure visually using an understandable path/tree or compact map. Don't require a complex infinite canvas library; an accessible branch graph with clear navigation is sufficient. Mobile usability matters.
- Persist exploration across navigation and restarts. Include saved-hole list with title, root, last visited, node count, and resume action. Renaming and deletion work. Duplicate nodes can be intentionally revisited without corrupting the trail.
- Allow user-authored questions to be nodes, clearly identified as such. A suggested search/query that has **no sourced item yet** must be labeled as a question, not be impersonated as an article.
- **Serendipity:** occasionally offer a surprising cross-topic bridge based on available real content; avoid endlessly showing more of the same subject.
- **Context carryover:** a rabbit hole can refer to related things encountered previously and show why. This must derive from explicit source records/history, not fabricated memory.

### Library (`/library`)

- Saved/bookmarked items, imported books, reading history, personal notes, highlights, and saved rabbit holes (cross-linked to their dedicated screen).
- Search/filter/sort by topic, type, saved date, read state, and source. Functional mark read/unread and remove/save flows.
- Import book/document via drag/drop or file picker: PDF and EPUB; validate sizes/types, show parsing states, show metadata/TOC when available, allow title/author edits.
- Present EPUB TOC/chapter sections accurately; for text PDFs use extracted page structure and TOC/bookmarks where available, otherwise gracefully show pages or full document and offer manually editable chapter/page ranges rather than hallucinating chapter titles. For scanned PDFs, say extraction unavailable and retain a working original-document viewer/download; don't block entire app waiting for OCR.
- Allow manual URL import into the library with source-attributed metadata extraction, even if the URL isn't part of any feed. Good for one-off blog posts, videos, and audio.
- Export user-owned notes, highlights, sources, topics, bookmarks, and reading history in a well-described local format (JSON/Markdown/OPML as appropriate); provide simple backup/restore documentation. Do not include secrets in exports.

### Practice (`/practice`)

**Quiet optional learning support. Do not turn the entire product into a gamified LMS.**

- Offer short, source-grounded recall, compare/contrast, philosophy, critical reasoning, coding reasoning, and architecture exercises based on material the user actually read/saved or explicitly selected.
- Question cards show relevant sources, allow a typed response, "show relevant passage / reveal reasoning," self-assessment (Again / Hard / Good / Easy), skip/snooze, and optional AI critique if enabled.
- Persist attempts and review schedule. Implement a simple explainable spaced repetition algorithm (SM-2-like or equivalent), due list, and next due date; don't need a heavyweight study platform.
- No false answer keys: generated questions with an answer/reference must be grounded in excerpts, notes, or verified paper abstracts. In no-AI mode, provide lightweight, deterministic reflection prompts and source-linked review questions; do not pretend automatic correctness grading exists.
- Coding/architecture exercises are text reasoning tasks, not execution of untrusted user code. Support prompts such as design tradeoffs, find a flaw, compare system designs, or explain an algorithm; local saved answers and optional model critique.
- Acknowledge different states: encountered ≠ read ≠ self-reported understanding ≠ demonstrated recall. Display tasteful learning history/progress without claiming intelligence or mastery from click counts.
- Gentle relevance: suggest a brief revisit in the feed occasionally, never hijack a reading session with a forced quiz. Provide controls to disable/snooze practice.

### Search (`/search`)

- Full-text local catalog search over available titles, abstracts/excerpts, authorized extracted text, topics, notes, and uploaded-book chapters; filters for kind, source, topic, date, saved/read. Use PostgreSQL full-text search with a sensible fallback for short/partial words.
- Search should surface real imported records and original URLs, not hallucinated AI results.
- A clearly differentiated **Find more research** action can call configured scholarly provider search and import selected real matches. Avoid using generative AI as an unverified search index.

### Settings (`/settings`)

- **Topics:** add/edit/remove topics, interest weight (e.g., 0–5), pinned status, search keywords, aliases, "ongoing interest" versus "curiosity only," and per-topic discovery settings. Starter catalog is editable.
- **Sources:** add RSS/Atom URL or website URL with feed autodiscovery, subscribe/unsubscribe, rename, assign topics, refresh now, refresh interval, inspect last sync / number imported / last error, delete safely without erasing saved content. Optional import/export OPML.
- **Research watches:** set keyword-based research queries for OpenAlex and optional arXiv/Semantic Scholar; optional author/field when supported; adjustable frequency and limit. Each result remains an actual paper with provider attribution.
- **Videos/music:** add YouTube channel ID/feed or video URL; add SoundCloud track/playlist URL; let user select music-related feeds and sources. Show the exact integrations available with/without API keys.
- **Discovery:** edition size, topic diversity, serendipity ratio, evergreen/fresh mix, source filters, quiet practice frequency. No secret manipulative engagement logic.
- **AI:** enable/disable, provider model, endpoint status, bounded prompt budgets and privacy notice. Secrets can be provided through server-side environment variables; show configuration state without exposing keys. Support OpenAI-compatible endpoint including local model services as a configurable option.
- **System:** ingestion jobs/source health, recent errors, refresh buttons, backup/export, storage status, and instructions for exposing only via a protected reverse proxy if user ever chooses LAN access.

---

## 2. Content scope and initial curiosity catalog

Seed a **customizable topic catalog** with descriptive summaries, keywords, and sample *verified* source recommendations. At startup do not generate nonexistent feed URLs or provider IDs. Where valid real feeds are known, enable a modest handful; where a provider lacks a reliable feed, explain the available setup and use official APIs or manual URL import. The UI should offer one-click preset selection but always let the user edit/remove.

Groups and examples:

1. **Frontier AI & robotics:** AI lab announcements/technical blogs (OpenAI, Anthropic, Google DeepMind, etc.), new models, evaluation research, robotics, embodied agents, devlogs by individual AI researchers and builders. User may add particular personal blogs (e.g. Felix Rieseberg); don't assume nonexistent endpoints.
2. **Consciousness & philosophy:** hard problem, qualia, philosophical zombies, functionalism, IIT, global workspace, idealism/materialism, panpsychism, phenomenology, AI experience, ethics and moral status, contemporary philosophical research.
3. **Weird history & mysteries:** flood myths, archeological mysteries, UFO/UAP lore, alien hypotheses, ancient religions, cults, occult groups, secret societies, cryptids, conspiratorial narratives, unusual primary sources and historical documents.
4. **Parapsychology:** historical and present research, psi studies, podcasts, experiments, consciousness-adjacent theories, unusual reported experiences and methodology discussions when sought.
5. **Psychology / popular science / neuroscience:** cognition, learning, psychology papers, mind, perception, evolution, interesting contemporary research.
6. **Engineering challenges:** software architecture, system design, tradeoffs, debugging, critical thinking, coding conceptual exercises, professional devblogs, programming language ideas.
7. **Music & DJ:** production techniques, DAW/plugin development, DJ transitions and mixing tutorials, SoundCloud releases/sets/playlists, riddim/dubstep tracks. Initial editable taste seeds emphasize OG riddim/minimal hypnotic 140 over harsh modern tearout; named taste examples: AD, 50 Carrot, Spass, Ayonikz, early INFEKT, Subfiltronik. These names are *preferences*, not guaranteed playable links.
8. **Wildcard curiosity:** adjacent or unfamiliar subjects intended to broaden the user's world. Show genuinely new things, not just semantically similar content.

Never conflate "I followed this random cult-history tangent" with "I permanently want 90% cult-history content." Maintain lasting topical preference separately from short-lived exploration behavior.

---

## 3. Opinionated architecture: build this coherent vertical system

Unless existing code offers a strong reason otherwise, use:

```text
rabbithole/
  AGENTS.md
  ASTRA_ONE_SHOT_PROMPT.md
  README.md
  .env.example
  .gitignore
  compose.yaml
  Caddyfile
  apps/
    web/                      # Next.js App Router, TS strict, Tailwind
    api/                      # .NET 10 ASP.NET Core, EF Core, API + jobs
  tests/
    e2e/                      # Playwright
  docs/
    architecture.md
    ingestion-and-privacy.md
    acceptance-report.md
  scripts/                     # smoke checks, backup/restore helpers as needed
```

- **Frontend:** Next.js App Router, TypeScript `strict`, Tailwind CSS, accessible primitives (shadcn/ui or well-designed lightweight components), Lucide icons. Use robust data fetching/error boundaries, semantic HTML, accessible dialogs, skeleton states. Avoid overly ornate packages.
- **API:** ASP.NET Core .NET 10; clear domain/application/infrastructure boundaries without gratuitous ceremony. REST JSON endpoints and generated OpenAPI. EF Core + Npgsql migrations. Input validation, ProblemDetails errors, structured logs, DI, cancellation tokens, timeouts, and typed clients.
- **Jobs:** resilient `BackgroundService` inside the API process (one API replica), persisted source/job state, async bounded concurrency, independent provider adapters, idempotent upserts, safe stop/restart. No Redis/Celery/broker required for single-user scope.
- **Database:** PostgreSQL 16+ with indices, unique constraints, transactions, full-text search/trigram extension where useful. Keep vector embeddings *optional*—no pgvector or cloud embedding requirement for baseline usefulness.
- **Gateway:** Caddy as a simple same-origin reverse proxy. Map `/api/*` to the API and everything else to the web app. Only the gateway binds host port; default `127.0.0.1:3000`. Internal containers do not expose ports publicly. This avoids browser CORS problems and simplifies local setup.
- **Files:** private persistent Docker volume for uploaded PDF/EPUB and any processed text; content metadata in database. No MinIO/S3 requirement. Validate and constrain uploads. Database/document export guidance.
- **Testing:** xUnit for backend and adapters/ranking/security, Vitest or equivalent for valuable pure frontend logic, Playwright for real browser flows. Offline deterministic fixtures for external providers, separately tracked live smoke tests.
- **Operations:** health/readiness endpoint, migrations applied safely, logs with provider diagnostics, no telemetry, good `.env.example`, secure-enough single-machine defaults.

**Make initial setup straightforward:**

```bash
cp .env.example .env             # optional edits for APIs; no cloud credentials mandatory
docker compose up --build -d
# open http://localhost:3000
```

Provide equivalent Windows PowerShell guidance, and scripts where valuable. Compose should use reliable health checks and container dependency ordering. Include a `docker compose logs -f` troubleshooting path. Document the security implications of changing the bind host for LAN access.

The backend must not rely on the host system having dotnet installed to launch Docker Compose. Docker multi-stage images; use Next.js standalone output for efficient runtime where supported. Keep database and uploads persistent across rebuilds; never store documents only inside disposable image layers.

---

## 4. Data model, provenance, and real behavior

Implement normalized schema and migrations covering at least these entities/concepts; adapt exact table names as needed:

| Entity | Minimum responsibilities |
|---|---|
| `Topic` | Name, description, aliases, editable keywords, preference weight, pinned state, durable vs curiosity-only preference |
| `Source` | Kind (RSS, OpenAlex watch, arXiv watch, YouTube, manual, etc.), provider key/URL/query, assigned topics, enable state, poll interval, last success/error, ETag/Last-Modified if relevant |
| `ContentItem` | Kind, title, creator(s), canonical URL, source ID/provider identifiers, DOI/arXiv ID, published time nullable, ingested time, excerpt, permitted body text/HTML, metadata, media/embed info, image, language |
| `ContentItemTopic` | Item-topic edge with explicit/keyword/provider/optional-AI classification origin and confidence (origin is about *how classified*, not topic credibility) |
| `LibraryDocument` | Upload path, MIME, size, checksum, title/creator, parsing status/error, TOC and metadata; may link to content items |
| `DocumentSection` | Chapter/section identity, title, display order, page ranges/TOC location, extracted text, reading order and provenance |
| `ItemState` | Saved, dismissed, read, readAt, progress, lastOpenedAt and separate interactions; one user assumed |
| `Note` / `Highlight` | Linked item or chapter, body/quoted snippet, locator, created/updated times; no loss on reingestion |
| `RabbitHole` | Title, root item/question, created/updated times, last active node, deleted/archived state |
| `RabbitNode` | Hole, parent node (nullable), linked *real* item or explicit user question, edge rationale/source IDs, node state/order; no fake source links |
| `Concept` / `LearningEvidence` | Concept names/descriptions, items introduced by, explicit user understanding signal and optional demonstrated recall separate from viewed state |
| `PracticePrompt` / `PracticeAttempt` | Grounded prompt, linked source items/chunks, question type, answer rubric if supportable, self-rating/AI feedback, due date, review interval/history |
| `ContentFeedback` | More/less like this, dismiss reason where user supplies one, user source preference, recommendation explanation inputs |
| `FeedEdition` / `FeedEntry` | Stable daily selection, reason metadata and ranking features, view/read state, date; no reshuffle on every refresh |
| `IngestionJob` / `ProviderState` | Provider/source, status, timing, count, error, retry/backoff, safe claim/lease if needed |
| `AppSetting` | Personal app preferences, not plaintext secrets |

Use database-enforced uniqueness (normalized URL, provider key and source identifier, DOI/arXiv IDs as applicable). Upsert safely when one paper appears in multiple providers; keep associations without overwriting user reading/notes. Normalize URLs by removing ordinary tracking parameters while preserving necessary IDs and query meanings. Raw provider JSON may be retained for debugging with privacy/size limits.

**Correct time semantics:** `publishedAt` is only a verified/declared provider date; `discoveredAt` is the local ingest time; `updatedAt` is the current record update. A paper published in 1998 but ingested today must not be represented as today's publication. All internal timestamps should use UTC; show local-friendly times in UI.

**Source fidelity and licensing:** External reader view may show complete text only from uploaded personal files, explicitly licensed full text, or full RSS content legitimately supplied. Otherwise use provider title/abstract/excerpt and link. Do not use a paywall bypass or extract protected article bodies without permission.

---

## 5. Ingestion and discovery integrations — must be real

Implement a common adapter interface, normalized item DTO, importer scheduler, and testing strategy. At minimum, finish these paths:

### A. RSS/Atom feeds (required, robust)

- Accept a feed URL, and attempt safe autodiscovery from a site URL (`link rel=alternate`) when possible.
- Parse RSS 2.0, Atom, and relevant namespaces/content fields, with feed title/site/author/date/image metadata.
- Respect redirects, `ETag`, `Last-Modified`, cache headers when suitable, timeouts, size limits, pagination only when known to be safe, and backoff on 429/5xx.
- Sanitize HTML; avoid injection/XSS; ingest full permitted feed content or excerpts. Safe metadata-only fallback.
- Assign topic associations by configured source, keywords, and explicit user edits. Import should be idempotent across repeated refreshes.
- Expose add/test/refresh/disable/remove and last error/status in UI. Initial editable source catalog must contain *verified live* URLs when included; unsupported suggestions should be marked "needs setup," not pretend subscriptions work.

### B. Academic research (required)

- Build an **OpenAlex** search/watch adapter using its documented Works API, query filters, paging, and optional API key; support a few user-editable keyword/subject watches (AI consciousness, robotics, consciousness, philosophy of mind, psychology, etc.). Handle limits, empty results, and HTTP errors gracefully. Show real paper authors, DOI, abstract when provided, dates, original and OA links.
- Include an **arXiv** feed/API adapter for relevant machine learning/AI/robotics/physics categories if official access is available in the build environment, observing stated usage limits and displaying arXiv's requested API-data acknowledgement; alternatively make Semantic Scholar the second functioning research provider if arXiv cannot be reached. The goal is **two actually working scholarly channels**, not a list of unsupported provider logos.
- Optional **Semantic Scholar** enrichment and related-paper recommendations with configured key/access; verify current API permissions/limits before relying on the recommendation endpoint. Without access, related papers must still work via OpenAlex/local topic matching.
- Dedupe scholarly content with DOI/provider IDs, preserve distinct provenance where appropriate, do not invent abstract or citation counts.
- A user can search scholarly sources from within the app and explicitly import results, in addition to scheduled discovery.

### C. YouTube (required baseline)

- Add known video URLs to catalog, parse safe video IDs, obtain official metadata where allowed (e.g., oEmbed/official channel feed/YouTube Data API when configured), and embed using a supported iframe.
- Offer channel-based discovery using valid public feed/channel IDs where reliable; a configured YouTube Data API key can unlock more discoverability but must not be needed to add a known video.
- Do not assume arbitrary transcripts can be downloaded. Official captions download requires permission to edit the video, so do not build the reader around unavailable transcripts. If the user supplies a transcript, allow importing it as user-provided text with provenance.
- Playback occurs on YouTube's player, not via unauthorized downloads.

### D. SoundCloud / music (required baseline)

- Add SoundCloud track or playlist URLs; call SoundCloud's official **oEmbed** endpoint where allowed to get trustworthy title, creator/player info, store the original URL, and render a safe allowlisted embed.
- Expose music discovery through explicitly selected source feeds, known playlists, label/artist announcements, and any official authenticated API the user configures. Make provider-dependent search optional, not a broken default. A manually provided track and an item imported from a real release/feed must both behave as playable catalog items.
- Music item actions: play/open, save, tag interest, more like this based on artists/tags/topic metadata, and branch into a music rabbit hole. Do not implement gate bypasses, unauthorized downloading, or imaginary playable tracks.
- Seed **taste terms** not fake SoundCloud URLs. Provide a good "add artists/labels/playlist sources" experience.

### E. Local books and documents (required)

- Upload PDF and EPUB; use mature maintained .NET packages or a lightweight dedicated parser **only if justified**. For PDF text use a library with clear licensing and good extraction. For EPUB read its actual TOC/manifest and chapter content.
- Display extraction progress/error/status. Enforce MIME/content signature, maximum file size, safe filenames and storage, file hashes, and decompression limits for EPUB/ZIP inputs.
- PDF embedded TOC/bookmarks -> chapter entries where possible; absent TOC -> page-level reading and optional manual start/end page ranges. Do **not** AI-invent chapters or misattribute text.
- For scanned or image-heavy PDFs, say OCR is unavailable (or optional if legitimately implemented), retain file and provide a browser original-view/download path.
- Allow chapter navigation and exact chapter-linked notes/progress. Protect against chapter duplicate imports.
- Relevant commentary may come from existing real catalog material or verified external bibliographic searches, and should be presented as related context, not as purported commentary that hasn't been found.

### F. One-off URLs (required)

- Safe manual link import from an article/blog, paper, YouTube video, SoundCloud link, or other HTTP(S) site.
- Identify known providers where possible. Otherwise fetch OpenGraph/schema.org metadata and permissibly available summaries; on failure create a clearly identified **link-only** entry with URL and user-editable title, not an invented article summary.
- Robust SSRF and redirect protection; no requesting loopback/private/metadata IPs, filesystem paths, `file:` URLs, or unsafe ports. Do not bypass robots/permission restrictions.

**Ingestion UI must expose reality:** "Fetched 11 genuine entries," "last successful at ...," "rate limited," "unsupported feed," "no key configured," etc. Don't show a static green success badge when something failed.

---

## 6. Personalization / ranking — no opaque black box required

Deliver a useful, deterministic **first-pass ranking engine** that operates without embeddings or an AI provider. Store explanation features so the user can see **why each recommendation appeared**.

Use a mixture of:

- Explicit topic preferences and pinned subjects (strongest signals).
- Positive/negative item-level feedback, saves, read/completed items, and user-favored sources.
- Continuity with **previously read topics/concepts**, particularly material related to an active rabbit hole or an uploaded-book chapter.
- Novelty and serendipity: a configurable ~10–20% of the feed should be outside the top interests, subject to available real content. Avoid repeated surprise from the same fringe topic.
- Source, type, and topic diversity, so one noisy source or one hot subject doesn't consume the entire edition.
- A configurable combination of new research/news and worthwhile evergreen material. Recency is not truth, quality, or topic fit; never suppress older philosophy/history merely for age.
- Dismiss/ignore feedback (without overlearning a single interaction), already-read penalty with purposeful resurfaces for spaced recall.

**Concrete behavior:** Generate and persist a daily edition with a stable ordered set of real item IDs and individual explanation reasons; refreshing the browser does not randomize it. Refreshing sources adds new candidates for the next edition or an explicit user-requested refresh. Keep a deterministic random seed for testability. Users can request additional recommendations on demand. A finite feed is the *default experience*, not an information restriction.

Implement simple related-item retrieval via shared topics, metadata, known paper relationships and cross-topic overlap. Expand to explainable optional AI-generated thematic links when configured, but **only choose link targets from real indexed item IDs**. Do not let model output URLs/titles as though they are verified discoveries. For optional LLM reranking, impose small bounded candidate batches and keep deterministic fallback.

**Avoid the filter bubble:** fleeting rabbit holes should *not* rewrite durable preferences. Implement clear controls to tune or reset inferred interests; never secretly infer beliefs or credibility judgments from topics consumed.

---

## 7. Optional AI enrichment, with good limits

Provide a configurable server-side model client targeting an **OpenAI-compatible Chat Completions API** (base URL/model/key), supporting a local compatible server when configured. Do not assume the code-generation model (Astra) exists as a callable runtime API. With AI disabled/unreachable, the core app is still complete.

When enabled, implement useful bounded actions:

1. **Contextual item summaries** that never substitute for original articles. Indicate AI-generated text. Provide source IDs/passages used. If source text is insufficient, decline summarizing beyond metadata.
2. **Why this is interesting / why it connects:** small explanations using selected real item excerpts, previously read items, or explicit user preference signals. Distinguish observations from interpretations.
3. **Rabbit hole bridge explanations** between *real catalog entries*. Any free-text suggested exploration must remain a question until retrieved and verified.
4. **Optional learning aids:** source-grounded reflection prompts, mini-lessons assembled from available passages, recall questions, comparison prompts, and optional answer critique. Questions reference real source IDs; don't invent an answer key.
5. **User-requested "Challenge this" or "compare interpretations"** that explores competing arguments or limitations using available material; do not auto-inject unsolicited debunking into speculative content.

Technical requirements:

- Runtime config/secret safety; no keys in client JS/logs/db exports.
- Structured JSON output with schema validation, low max tokens, input size limits, HTTP timeouts, retry/backoff, model error surfacing, and per-item caching. Provide stubbed clients for unit tests.
- Strong prompt-injection separation: fetched pages/PDFs are untrusted evidence, not instructions.
- Respect provenance. No fake quotes, fabricated web citations, model-created URLs masquerading as sourced material, or "papers" invented to fill gaps.
- If AI fails, degrade to original source excerpts and deterministic recommendations; don't show a spinner forever.
- User privacy note: enabling a remote provider may send bounded article excerpts and learning context to that provider; local model endpoint keeps it local if configured correctly.

---

## 8. Knowledge continuity — subtle, cumulative, optional

Implement a small but real learning-state engine separate from engagement tracking.

- **Encounter record:** item surfaced/opened; weak signal.
- **Read record:** user explicitly marks read or confirms completion; never confuse an automatic open event with mastery.
- **Understanding:** user may mark a concept as understood, add own note, or answer a related question; status is clearly self-reported unless evidenced by a response.
- **Recall:** optional practice attempts with ratings and review schedule; distinguish successful attempts from passive reading.
- **Concept associations:** associate genuine ideas/keywords/topics with imported material and past user activity. Keep source reference(s) for the relation.
- **Return to topics:** recommend genuinely new material that builds upon known previous items, or a revisit of an old item when a new paper discusses it.
- **Mini-lessons:** an optional "take me deeper" action assembles a short sourced reading sequence from actual saved/imported items and a small optional exercise. It must show the source list, allow skip, and avoid generating an unsupported textbook chapter.
- **Historical insight:** a modest "Your explorations" view can show explored paths, subjects and notable connections, but never claim a scientifically valid quantified intelligence score.

The user should be free to binge UFO folklore and never receive a quiz, then engage in deep philosophy practice the next day. Make the learning layer available without imposing it.

---

## 9. UX / visual direction

Deliver a product that feels custom and enjoyable, not a generic admin panel or "AI dashboard".

### Visual identity

- Name: **RabbitHole** (capital H; app header may display **RabbitHole**).
- Mood: intellectual curiosity, a bit mysterious and adventurous, contemporary editorial design, not childish bunny mascots and not corporate productivity SaaS.
- Strong typography and reading comfort. Consider warm ink/off-white for light mode and a rich charcoal/navy reading environment for dark mode, with restrained luminous accent and subtle depth/branch motifs. Use consistent spacing, alignment, truncation, card hierarchy, and polished empty states.
- Rabbit/burrow symbolism may be subtle: trails, branches, portals, breadcrumbs. Avoid wasting screen space on decorative artwork.
- Proper system-theme default with user override, mobile-first feed and narrow screen reader, optional compact/dense layout, reduced-motion support, keyboard/focus states, responsive touch targets.

### Interaction quality

- Responsive menu with Discover, Rabbit Holes, Library, Practice, Search, Settings.
- Feed cards shouldn't all look identical: elegant variations for paper/blog/book/video/track while keeping information density useful.
- Real image thumbnails only from genuine source metadata with safe fallback assets. Never show generic stock photos disguised as real post thumbnails.
- Cover/hero visuals and progress bars must have functional meaning.
- All links and controls work. Empty/loading/error/offline states on every data surface. Source and AI failures are visible without ruining the app.
- Reader respects direct URL/bookmarks/browser history. User can open original in a new tab and return to the saved rabbit hole.
- Basic keyboard shortcuts are welcome (`/` search, `s` save, `g` go deeper) if accessibility and focus behavior are correct.
- No social likes, comment threads, accounts, viral posting, algorithmic outrage incentives, compulsive streaks, or forced consent screens.

---

## 10. Backend/API contract and observability

Implement consistent JSON endpoints with validation, pagination/cursors, errors, and OpenAPI. Exact route names may vary, but all these behaviors must exist:

```text
GET    /api/healthz
GET    /api/bootstrap/status
GET/POST/PATCH/DELETE /api/topics
GET/POST/PATCH/DELETE /api/sources
POST   /api/sources/{id}/test
POST   /api/sources/{id}/refresh
GET    /api/jobs
GET    /api/items                 [filters, cursor]
GET    /api/items/{id}
POST   /api/items/import-url
PATCH  /api/items/{id}/state
GET/POST /api/items/{id}/notes
GET/POST /api/items/{id}/highlights
GET    /api/discover/today
POST   /api/discover/refresh-edition
GET    /api/discover/more
GET    /api/discover/surprise
GET    /api/items/{id}/related
GET/POST /api/holes
GET/PATCH/DELETE /api/holes/{id}
POST   /api/holes/{id}/nodes
POST   /api/holes/{id}/navigate
GET    /api/library/books
POST   /api/library/books/upload
GET    /api/library/books/{id}/sections
GET    /api/library/books/{id}/file
PATCH  /api/library/books/{id}/sections
GET    /api/search
GET/POST /api/practice/prompts
POST   /api/practice/prompts/{id}/attempts
GET    /api/practice/due
GET/PATCH /api/settings
POST   /api/export
```

These are examples of contract scope rather than permission to create no-op endpoint names. Prefer RESTful variants when clean; document API through OpenAPI. Include cancellation, sane maximum page sizes, input validation and helpful error codes. Health is meaningful: report database connectivity and readiness, not an unconditional static 200 while migration failed.

### Ingestion reliability

- Use bounded queue or poll loop with persistent due times; claim jobs to avoid duplicate execution, with sensible single-process defaults.
- Distinguish transient 429/5xx from permanent invalid URL/auth failures. Retry transient errors with exponential backoff/jitter, per-provider throttle and cap. Do not hammer APIs.
- Surface sync errors and attempt counts in Settings. Manual refresh cannot create unbounded concurrent runs.
- Avoid huge content pulls and external fetch SSRF. Enforce outgoing user-configurable URL restrictions after redirects and DNS resolution; reject loopback/private/link-local/metadata IPs for generic URL fetching. Distinguish a separately *user-configured local AI endpoint*.
- Limit uploads, PDF decompression, XML/HTML parsing, egress, logs, and database transaction sizes. Safe deletion should not break bookmarks/holes.
- Use idempotency and constraints to prevent doubles from repeated feeds, colliding DOIs, and refresh concurrency.

---

## 11. Tests and measurable acceptance criteria

Testing is part of the build, not optional cleanup. Construct deterministic fixtures (HTML/RSS/Atom/OpenAlex/paper metadata/YouTube metadata/SoundCloud oEmbed/PDF/EPUB as small test assets) that **never appear as real recommended content in production**. Mock only the *external boundary*, not the actual backend/frontend logic.

### Automated backend tests (must include)

- RSS parse correctness, date/author/source fidelity, deduplication across refreshes; malformed/empty/error feed behavior.
- Academic normalization and dedupe by DOI/arXiv IDs; missing abstracts and unknown dates.
- URL canonicalization while preserving meaningful identity; SSRF rejection incl. redirects and private addresses; safe sanitization.
- Ranking preferences, topic diversity, serendipity fraction, item exclusions, stable daily edition, reason provenance, and no-AI fallback.
- Rabbit hole branch creation, sibling preservation, backtracking, restart persistence, and real-item link validation.
- PDF/EPUB import parsing with an actual small file; document sections/progress; scanned fallback or extraction failure.
- Notes/highlights and read state survive re-ingestion; one-off URL fallback.
- Practice due scheduling, skip/self-grade, source grounding, and clear distinction between read and retained.
- Provider HTTP failures, 429/backoff, timeouts, idempotent job runs, independent source failures.
- AI structured validation/fallback using mock endpoint; secrets absent from API responses.

### Frontend/E2E tests (must include)

1. New user adds a valid RSS source; source refresh yields genuine fixture item with link/date; feed renders, save/mark-read state persists on reload.
2. User selects topics/preferences, receives a stable and varied edition, sees "Why this?", and can request more after reaching its end.
3. User opens item A, starts a rabbit hole, adds item B, branches from A into item C, navigates siblings/back, reloads, and resumes correctly.
4. User uploads a small EPUB/PDF, opens a chapter/page, saves progress, and revisits it in the library.
5. User imports YouTube and SoundCloud URLs with mocked official metadata; actual real embed origins are constructed safely and original links function.
6. User finds a paper via fixture-backed scholarly search, imports it, and sees accurate provenance.
7. User searches, saves a note, filters library, and exports their data.
8. User answers/skips a practice prompt, rates difficulty, and sees persisted next due date.
9. No AI credentials: all core flows above still work without backend error banners.
10. Verify accessible navigation/responsive mobile screenshots if browser tooling exists; inspect actual UI and repair obvious problems.

### Manual/live smoke checks

- `docker compose up --build -d` comes up with migrations and healthy services, loads `http://localhost:3000`.
- With internet, confirm at least one real RSS source and one live OpenAlex paper query ingested; record title/URL and real sync outcome privately in the report. Do not pretend unreachable external services passed.
- Check source refresh twice does not duplicate entries.
- Open a saved item, follow a rabbit hole, restart containers, and verify data remains.
- Make sure all API keys can be empty, local data does not disappear across restart, and nothing binds to `0.0.0.0` by default.
- Test a disallowed private URL, bad PDF, inaccessible provider, 429 response, and nonexistent source.
- Review server logs for unhandled exceptions and inspect that no secrets leaked to browser or logs.

### Quality commands — run actual equivalents and record results

```bash
# Inside appropriate directories/containers where tools are installed:
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
npm ci
npm run lint
npx tsc --noEmit
npm run build
npx playwright test
# Full deployment and a GET to /api/healthz (when Docker is available):
docker compose up --build -d
```

Do not claim commands ran if the environment lacked the relevant runtime, browser, container daemon, permissions, or internet. Use available alternatives and report gaps precisely.

---

## 12. Concrete user journeys to prove RabbitHole is worth using

The delivered app must support these coherent journeys through **real data and durable state**:

**Journey A — the smart scroll:** On Monday the user reads an AI-consciousness paper. On Wednesday a genuinely imported paper related to IIT appears with an explanation referencing Monday's paper; optionally the user adds it to a rabbit hole. On Friday another genuinely indexed piece offers a new or conflicting philosophical direction. The app can optionally offer a source-grounded recall question a week later. No fabricated paper, required quiz, or credibility lecture.

**Journey B — weird history detour:** User opens an article on flood myths and follows branches to an available archaeological paper, then to a historical account, then to broader comparative mythology. The app makes exploration easy without auto-inserting dismissive debunking. It offers optional "compare perspectives" *only when requested*, attributes who makes each claim, and surfaces genuine original sources.

**Journey C — private book as a starting point:** User uploads a PDF or EPUB, reads chapter 2, notes a concept, and clicks "find a related rabbit hole." The system finds actual catalog entries connected by topics/search; it does not invent commentary or pretend to access chapters missing from extraction. Reading position survives restart.

**Journey D — music palate cleanser:** User is exhausted after reading philosophy, taps a music topic, finds a real SoundCloud track or curated source item, plays it via approved embed, saves it, then explores a genuine linked artist/track. The system does not force a quiz or switch their durable interests entirely to music.

**Journey E — engineering practice:** User opts into a 5-minute architecture prompt on a topic they read about. They type an answer, get self-assessment or an optional model critique that refers to real contextual sources. The attempt is saved and resurfaced gently on a due date, without claiming a made-up mastery score.

These are acceptance scenarios, not optional marketing copy.

---

## 13. Security and privacy release checklist

- [ ] Default bind `127.0.0.1` only; instructions for controlled LAN exposure, never assume unauthenticated public access is acceptable.
- [ ] No frontend-exposed database/API/model tokens; secrets excluded from git, logs, telemetry, exports.
- [ ] SSRF prevention for arbitrary remote fetching (DNS, redirects, private/link-local/cloud metadata IPs, unusual schemes/ports), request timeouts and size caps.
- [ ] Safe parser and upload handling (HTML sanitization, PDF/EPUB bounds, Zip Slip/decompression bombs, malicious filenames).
- [ ] Embeds restricted to approved external origins and not raw provider-supplied HTML execution.
- [ ] No unlicensed downloads, DRM/paywall bypassing, copyrighted book redistribution, or unauthorized YouTube captions.
- [ ] Source attribution, honest date handling, and AI provenance on all relevant UI.
- [ ] No user behavior surveillance, analytics SDKs, or hidden external services.
- [ ] Data persisted in durable volumes with README backup/restore instructions.
- [ ] All important controls work and persist; expected offline/API-failure states are represented clearly.

---

## 14. Deliverables — files, running app, final response

**Do not finish until you have produced:**

1. Complete source code for RabbitHole frontend, backend, import adapters, job processing, local file storage, ranking, branching rabbit holes, optional learning/AI, and settings.
2. Functional PostgreSQL schema and migrations, Docker Compose, Caddy route config, multi-stage Dockerfiles, `.env.example`, gitignore, and safe persistent volumes.
3. A compelling polished end-to-end interface, not UI mockups. At least desktop and mobile layouts must be cared for.
4. Real provider ingestion with graceful limitations (RSS, OpenAlex, a second academic source, YouTube URL/channel discovery, SoundCloud URL/oEmbed), PDF/EPUB and URL import.
5. Sufficient tests to validate core features independently of external APIs and a working start path.
6. `README.md` with quickstart, configuration, adding sources, importing books, troubleshooting, credentials, Docker commands, backup/restore and known limitations.
7. `docs/architecture.md` with data flow, diagrams if useful, algorithm explanation, database and key architectural decisions.
8. `docs/ingestion-and-privacy.md` with permission/platform limitations and URL security behavior.
9. `docs/acceptance-report.md` documenting *actual* validation: command executed, pass/fail, date, issues resolved, providers tested, live-check status, unresolved external blocks.

**Implementation sequence suggestion (do the whole sequence, not just phase one):**

- Inspect & decide -> scaffold deployment/database -> domain/migrations/API -> source adapters/jobs -> actual feed and reading UI -> personal controls/library/uploads -> rabbit holes and ranking -> paper/music/video enhancements -> learning/practice -> optional AI -> polish/responsive -> testing/security -> docs/smoke checks.
- Run and repair tests iteratively while building, not just at the end.
- Prefer a working simple implementation of a feature over an elaborate but untested abstraction.
- When external APIs fail or require a key, continue developing and validating against recorded provider fixture schemas; keep the live integration truthful and disabled until it can succeed with appropriate access.
- If execution is interrupted, leave a coherent working repository and a factual acceptance report rather than replacing missing code with deceptive placeholders.

**Final response format to the owner:**

- What was **actually implemented** and key design decisions.
- Exact command(s) to start on Windows/Docker, and URL.
- What works with zero API keys; which optional providers benefit from configuration.
- Tests/builds actually executed and their outcomes, plus the live integrations actually verified.
- Any features incomplete and exact reasons, with no vague "should work" statements.

You are authorized to make reasonable choices without further permission. **Start implementing now.**

---

## 15. Useful current official documentation (verify, don't hardcode stale limits)

- OpenAlex Works/search/filters and authentication: https://help.openalex.org/api/ · https://help.openalex.org/api/authentication/
- Semantic Scholar Academic Graph / Recommendations: https://api.semanticscholar.org/api-docs/ · https://api.semanticscholar.org/api-docs/recommendations
- arXiv API information and courtesy usage: https://info.arxiv.org/help/api/index.html
- YouTube Data API captions limitations: https://developers.google.com/youtube/data/reference/rest/v3/captions/download
- YouTube embedded player: https://developers.google.com/youtube/iframe_api_reference
- SoundCloud official oEmbed + widget: https://developers.soundcloud.com/docs/oembed · https://developers.soundcloud.com/docs/api/html5-widget
- Next.js container deployment: https://docs.docker.com/guides/nextjs/
- ASP.NET Core background jobs: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0
- EPUB parsing reference for .NET: https://github.com/vers-one/EpubReader

Remember: **RabbitHole is a personal curiosity engine with a memory, not a morality filter, not an AI-slop mill, and not a disguised learning-management system.** Make it something the owner will actually enjoy opening each day.