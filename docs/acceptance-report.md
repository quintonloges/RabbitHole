# RabbitHole acceptance report

## Recommendation quality pass — 18 October 2026

Implemented concept-based related ranking, editable concept tags, path-aware exclusions and intentional revisits, bounded routine-announcement penalties, structured explanations and per-trail/per-turn provenance. Curated/example/unknown-origin nodes and passive opens no longer become personal exploration evidence. No source, external integration, AI dependency or credibility policy was added.

The starting catalog was **276 items**, with 35 configured sources (23 active), eight topics, seven personal-state rows, two curated trails and ten nodes. The additive migration `20261018041856_RecommendationEvidence` and audited provenance backfill preserve every original item/state/graph field and ID. The current legacy edition was archived by day key with all entries retained, and a new 25-item edition was created. The full comparison, actual titles/IDs and limitations are in [recommendation-quality-report.md](recommendation-quality-report.md).

Actual verification:

- `dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release --no-restore` — **159 passed**, zero failed/skipped; formatting and EF pending-model checks passed.
- Frontend lint, TypeScript and production build passed. `npm run test:e2e` — **15 passed** in 42.2 seconds; the recommendation test passed again after the final scoped mobile layout adjustment.
- `python -m unittest discover -s tests/scripts -v` — **7 passed**.
- `docker compose up --build -d --wait` — final images built and all four services running; API/web/database health checks pass. The gateway serves the app and remains the only published host port, `127.0.0.1:3000`.
- `./scripts/check-recommendations-postgres.ps1` — **2 HTTP tests passed** against a disposable copy on the actual PostgreSQL 17 server, with real note/state/tag/branch persistence and stored daily evidence. The test database was removed; production received no test fixtures.
- All 276 final live related orders/reasons and seven three-level walks matched the frozen-catalog evaluator. The original catalog/settings fingerprint is unchanged. The old API re-offered 17 previously visited candidates in those walks; the path-aware output re-offered none.
- Pre/post migration export comparison passed with only the documented edition-key archive allowed. A complete service stop/start followed by an exact export comparison also passed, including current positions and structured rationale. All **276 items, two trails and ten nodes** remain.
- Eight real-catalog UI checks at desktop 1440px and mobile 390px passed with no overflow, JavaScript errors or failed HTTP responses. Screenshots and manual browser inspection covered Discover, expanded explanations, reader recommendations, saved trail navigation and explicit revisit labels. A temporary read-through proxy suppressed passive opened writes during visual QA.
- Production SQL checks found zero duplicate canonical URLs and zero test-fixture titles. Recent runtime logs had zero application failure-pattern matches. `git diff --check` passed.

An intermediate mobile Settings overflow was fixed and the complete browser suite rerun successfully. No provider refresh or AI call was part of the frozen-catalog comparison. Sparse metadata and the limited concept vocabulary still constrain Remote Viewing/UFO paths, and format heuristics do not identify every promotional item; these are documented for human evaluation. The pre-change database/uploads backup is `backups/20261018-000906`.

## Curation review fixes — 17 October 2026

All three findings from the review of `d849ff0` are fixed with permanent regressions:

- The reading-pack installer checks primary identifiers, stored provider references and known OpenAlex/arXiv alias fields before calling import. Existing records retain their edited metadata; malformed metadata and identifiers from other providers do not produce false matches.
- Explicit RSS/Atom selection searches the entire size-limited feed for the requested URL, including entries after position 100. It normalizes/imports only the selected entry. Ordinary refresh parsing retains its 100-entry cap, and both direct feeds and HTML autodiscovery retain the existing fetch/XML protections.
- Failed or interrupted refresh jobs for existing subscriptions produce a visible installer error and a nonzero exit code. Successful zero-import refreshes and existing subscription preferences are preserved.

The new regressions failed against the reviewed implementation before these fixes. The final checks were:

| Command or check | Actual result |
|---|---|
| `python -m unittest discover -s tests/scripts -v` | **7 passed**. Includes alias preservation in preview/apply modes, malformed metadata, missing-item import, repeated selection, refresh failures and opt-in flags. Added to `scripts/check.ps1`. |
| `dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release --no-restore -v:minimal --blame-hang-timeout 60s` | **134 passed**, 0 failed/skipped. Four new cases cover entry 101 in direct/autodiscovered RSS and Atom, retaining the normal refresh cap and importing no unrelated records. |
| `dotnet format RabbitHole.slnx --verify-no-changes --no-restore` | Passed after correcting existing initializer whitespace in `CurationTests.cs`. |
| `dotnet build RabbitHole.slnx -c Release --no-restore -v:minimal` | Passed with zero warnings/errors. |
| `npm --prefix apps/web run lint`, `npm --prefix apps/web run typecheck`, `npm --prefix apps/web run build` | All passed; production routes built successfully. |
| `npm run test:e2e -- --reporter=line` with `CI=1` | **14 passed** in 40.3 seconds, using a fresh isolated fixture API and browser. Covers persisted user state, branches, documents, media, error handling and responsive navigation. |
| `docker compose up --build -d --wait` | Passed. Rebuilt API/web containers and the existing database/gateway are healthy. Gateway remains the only published host port, `127.0.0.1:3000`. Catalog count was **276 before and after** the rebuild. |
| Both pack scripts without `--apply`, with reports under `.cache/review-d849ff0/` | Both exited 0: **23/23 existing subscriptions** and **27/27 existing selected items** recognized and preserved. These plans made no import or settings requests. |
| Configured Quanta source's `/test` endpoint, health and runtime logs | Live feed parsed **5 records** without importing; API health returned `ok`; recent API/web logs had zero error-pattern matches. |
| `git diff --check` | Passed. |

Local test-host/build-host pipes, Docker access and generated Next.js directories required execution outside the sandbox. Failed sandbox attempts are not counted as successful checks. No provider contract or database schema changed in this fix pass.

## Personal feed curation — 17 October 2026

The later population pass left the actual local PostgreSQL instance with **261 real records, 23 healthy active sources, 221 Discover-eligible records, and two saved branching trails**. All nine original item objects, the original reading state, eight topics, settings, and three original subscription preferences were preserved. See [feed-curation-report.md](feed-curation-report.md) for the 45-record editorial audit, source endpoints, exact schedules/counts, seven three-level recommendation walks, and provider restrictions. Earlier sections below describe the earlier nine-record acceptance baseline.

Actual final checks in this pass:

- Backend: `dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release --no-restore -v:minimal --blame-hang-timeout 60s` — **130 passed**, none failed/skipped.
- `dotnet format apps/api/RabbitHole.Api.csproj --verify-no-changes --no-restore` — passed.
- `npm --prefix apps/web run lint`, `npm --prefix apps/web run typecheck`, and `npm --prefix apps/web run build` — passed.
- `npm run test:e2e` — **14 passed** in 40.2 seconds, on an isolated fixture database; includes responsive/mobile checks.
- `docker compose up --build -d --wait` — full production build/start passed. The final API-only rebuild also passed.
- `docker compose restart db api web`, then `docker compose up -d --wait` — all services healthy; pre/post exported persistent rows and source preferences equal. Only `127.0.0.1:3000` is published.
- Repeat refreshes of all 22 newly activated sources completed with **zero new records** (261 before/after); source and reading pack reapplication preserved all matches.
- Zero duplicate nonempty canonical URL/DOI/arXiv identity values; no test-fixture titles in production.
- Live browser: populated Discover, saved-trail backtracking/siblings, correct unknown-date/source notes, actual SoundCloud/YouTube players, and Settings keyless-watch guidance inspected. Audible playback was not asserted.

No AI/OpenAlex/YouTube API credentials were configured. OpenAlex watches now explicitly sample the first page without a key and can miss busy-query results; authenticated cursor continuation remains tested. New arXiv watches, a DNS-blocked Variety of Sound feed, and an empty psi query remain disabled. The unavailable 2023 AI-consciousness paper is documented rather than claimed imported. Four newly imported low-fit records were passed on, while all retained data remains searchable.

## Earlier acceptance baseline

Verified on **17 October 2026**, on Windows with Docker Desktop Linux containers. This report distinguishes the review-remediation checks from the earlier initial-build evidence. No AI credentials were supplied.

## Review remediation

All twelve review findings have permanent regression coverage and fixes:

1. RSS GUIDs have feed-scoped identities. Replacing a subscription's feed creates a new namespace and retains the previous origin.
2. Bridge records merge all matching identities transactionally, retaining old IDs as aliases and preserving personal data. Failed database writes are rolled back and detached before retryable health is saved; later sources still refresh.
3. Explicit export projections recursively redact credentials in provider IDs, metadata and encoded nested player URLs without mutating stored records.
4. Multiple source origins persist for each item; disabling the primary source cannot hide an item supplied by another enabled source.
5. Explicit topic inclusions and removals are stored independently of automatic assignments and survive refresh and document retry.
6. Shared keyword classification covers external imports and local text, and edits reclassify the existing catalog.
7. Interrupted document imports recover by same-file upload or Retry extraction, preserving document/section IDs, notes, progress and edited metadata.
8. Practice drafts and delayed AI results belong to individual prompts; Not now cannot save another prompt's draft.
9. Saved/read UI changes reconcile parent caches after successful persistence and retain failure feedback.
10. OpenAlex watches use chronological, bounded cursor pages with durable checkpoints; search remains relevance ranked.
11. arXiv abstracts retain the supplied summary as source text, separately from the short excerpt.
12. Internal metadata timeouts produce honest link-only imports; caller cancellation still propagates.

The source-edit review additionally caught and fixed feed-namespace reuse, stale watch checkpoints after query edits, and overwriting retired origin snapshots. The first combined backend run exposed a legacy explicit topic choice being reset during document retry; that was corrected before the successful full rerun.

| Review-pass command or check | Actual result |
|---|---|
| `dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release --no-restore -v:minimal --blame-hang-timeout 60s` | **115 passed, 0 failed, 0 skipped.** Includes 60 additional cases covering all review findings, legacy SQLite upgrades, endpoint behavior and fault injection. Local test-host IPC required execution outside the sandbox. |
| `npm run test:e2e -- --reporter=line` | **14 passed**, final browser run 41.2 seconds; seven added regressions cover mutation races, draft isolation, late AI results and document recovery. |
| `dotnet build RabbitHole.slnx -c Release --no-restore -v:minimal` | Passed; backend/test compilation also passed in the complete Release test run. |
| `dotnet format RabbitHole.slnx --verify-no-changes --no-restore` | Passed. |
| `npm --prefix apps/web run lint`, `npm --prefix apps/web run typecheck`, `npm --prefix apps/web run build` | All passed. Docker also built the production frontend successfully. |
| `dotnet ef migrations has-pending-model-changes --project apps/api` | No pending model changes. EF tool 10.0.12 reports its patch-version difference from runtime 10.0.13; generation/checks succeeded. |
| `./scripts/backup.ps1` | Pre-upgrade database and uploads saved to ignored `backups/20261017-202352`; original API restarted. |
| `./scripts/verify-restore.ps1 -BackupFolder ./backups/20261017-202352` | Passed in an isolated Docker project: old PostgreSQL backup upgraded, nine real items restored, note search, saved/read/progress, sibling branches, Linux PDF extraction, original-file SHA-256, export and database/API/web restart verified. Only temporary verification volumes were removed. |
| `docker compose up --build -d --wait` | Passed against the main installation after isolated verification; forward migrations/backfill applied, API/database/web healthy, gateway remains loopback-only. |
| Pre/post-upgrade export comparison and runtime logs | All nine existing item IDs and original title/author/URL/provider IDs/text/date/document fields retained; existing state, notes, documents/sections, trails, practice and editions unchanged. Backfilled 24 identities and nine origins. Health returned `ok`; inspected logs contained no error-level/unhandled-exception matches. |
| `git diff --check` | Passed with the repository's normal Windows line-ending configuration. |

Legacy SQLite regression tests construct the original schema, preserve populated user state and references through repeated schema/backfill runs, and check foreign keys, identity indexes, topic overrides, unknown legacy tags and malformed metadata. The PostgreSQL restore check exercises the actual production migrations against a pre-upgrade backup. Official OpenAlex sorting/filtering/paging and arXiv abstract documentation were consulted; this review pass made no new live AI or research-provider smoke request. Earlier live-provider evidence is retained below.

## Delivered application

The running app is at **http://localhost:3000**. Only the Caddy gateway publishes a host port, bound to `127.0.0.1:3000`. The API, web server and PostgreSQL are healthy. EF migrations create the relational schema, normalized identity registry, source origins, topic decisions and PostgreSQL full-text indexes. Original uploads and PostgreSQL use separate persistent Docker volumes.

Implemented vertical flows:

- A finite, persistent daily edition with real source metadata, topic pivots, rationale, save/read/dismiss and feedback, deliberate more-items selection, surprise and links to resume saved trails.
- Editable lasting interests versus passing curiosity; source subscriptions, research watches, refresh/test controls, health, jobs and retry backoff.
- RSS/Atom and autodiscovery, OpenAlex and arXiv research, YouTube channel/video metadata, SoundCloud official oEmbed, and metadata-only manual URLs with an honest fallback.
- Item reading, available paper abstracts, DOI/arXiv/provider identifiers, returned venue/access/citation links, notes/highlights and source provenance. Feed-supplied content and AI synthesis are separate.
- Persistent branching rabbit holes with sibling navigation, user questions, renaming/deletion and last-position resume.
- Private PDF/EPUB import, real pages/sections/TOC, original viewing/download, progress, metadata edits, user-defined PDF ranges and duplicate-file detection.
- Local PostgreSQL full-text/lexical search, notes search, type/source/topic/saved/read/date filters, and library navigation.
- Explicitly enabled practice with five source-grounded prompt kinds, passage reveal, saved responses, self-rating, snooze and a persisted review schedule. Encountered, read and understood remain separate.
- Optional server-only OpenAI-compatible enrichment, validated citations/quotes, bounded requests, caching and failure fallback. JSON export and database/files backup/restore.

The main collection contains **nine real provider records and zero test fixtures**. Quanta is enabled daily; the two research watches used for live checks remain disabled after manual refresh. Fresh installations seed editable topics, not fictitious content or automatic source subscriptions.

## Initial-build checks (before review remediation)

| Command or check | Actual result |
|---|---|
| `dotnet build RabbitHole.slnx -c Release --no-restore -v:minimal` | Passed, zero warnings/errors. A sandbox-restricted attempt failed before compilation; the normal local-process run passed. |
| `dotnet format RabbitHole.slnx --verify-no-changes --no-restore` | Passed after whitespace corrections. |
| `dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release --no-restore -v:q` | **55 passed, 0 failed, 0 skipped.** |
| `npm --prefix apps/web run lint` | Passed. |
| `npm --prefix apps/web run typecheck` | Passed. |
| Docker web build: `npm ci`, `npm run build` | Passed. Final Next 16.4.2 webpack production build generated all routes. |
| `npm run test:e2e` | **7 passed**, final run 31.3 seconds. Six complete user journeys plus a publication-date regression. |
| `npm --prefix apps/web audit` and root `npm audit` | Zero reported vulnerabilities. |
| `dotnet list apps/api/RabbitHole.Api.csproj package --vulnerable --include-transitive` | No vulnerable packages reported by the configured NuGet source. |
| EF migration generation, idempotent SQL generation and pending-model check | Successful; no pending model changes. Docker applied the migration to PostgreSQL. |
| `docker compose up --build -d` | Passed. PostgreSQL, API, web and loopback gateway started; later rebuilds retained the catalog. |
| `GET /api/healthz`, production catalog and log inspection | `ok`; nine records; no fixture markers; no error-level/unhandled-exception matches in the final inspected logs. |
| `./scripts/backup.ps1` | Passed; database dump and uploads directory saved under ignored `backups/20261017-172123`. API restarted afterward. |
| `./scripts/verify-restore.ps1 -BackupFolder ./backups/20261017-172123` | Passed in a unique temporary Docker project. Restored nine real items; verified PostgreSQL note search, saved/read/progress state, sibling branches/current position, Linux PDF parsing, export and original-file SHA-256 after database/API/web restart. Temporary containers/volumes removed. |
| Production browser inspection | Seven routes loaded without browser errors; inspected Discover, reader, Library, Rabbit Holes, Practice, Search and Settings. Desktop, 390px mobile and dark-mode screenshots captured locally. |
| Git/export privacy checks | `.env`, documents, backups and build outputs ignored; no private-key/API-token pattern found in deliverable files. Export tests exclude credentials/private storage names. |

The E2E host runs the real API and UI with temporary SQLite storage and replaces only the external HTTP boundary. It cannot seed the production database. Real small PDF/EPUB fixtures exercise the parser worker. The Docker restore verification separately exercises PostgreSQL and Linux parsing.

Backend coverage includes malformed/empty/encoded feeds; repeat refresh and conditional 304; identifier deduplication; safe URL/DNS/redirect handling; private, mapped and 6to4 addresses; timeouts, 429/backoff and failure isolation; concurrent refresh; source deletion preserving personal state; stable ranking/diversity/serendipity; sibling branches; document bounds and extraction fallback; review scheduling; malformed AI output; citation/quote validation; opt-in gating and secret-free exports. OpenAlex abstract reconstruction rejects malformed, sparse, conflicting or excessive positions and bounds citation metadata.

Browser journeys exercise preferences, real fixture import and repeat refresh, stable/more editions, save/read reload, note search and filters, A→B/A→C branches, questions and resume, EPUB/PDF upload and progress, safe click-to-load media, paper import/abstract/DOI, practice passage/response/rating/architecture prompts, failure responses, export, and mobile navigation without horizontal overflow.

## Live providers

The complete source titles, URLs, dates and source responses are recorded in [provider-verification.md](provider-verification.md).

| Integration | Live evidence |
|---|---|
| Quanta RSS | Three actual feed items imported into PostgreSQL. Repeat refresh imported zero; stored feed text verified through the item detail endpoint. |
| OpenAlex | Three actual consciousness/philosophy records imported, including `W2056981297`. Repeat refresh imported zero. Final rebuild refresh completed with zero new items and retained exact source dates `2007-06-01`, `1986-04-01`, `1985-08-01`. |
| arXiv | Three actual search records imported. Repeat refresh imported zero. |
| YouTube | Official oEmbed returned valid metadata for Google's example video. Channel/video parsing and safe player construction passed fixture tests. Live in-app playback was not asserted. |
| SoundCloud | Official oEmbed returned valid metadata for its documented example track. Fixture-backed import/widget construction passed. Uninterrupted live playback was not asserted. |
| Suggested feeds | OpenAI News, Quanta and arXiv cs.AI returned valid live feed content types. The tested nonexistent Anthropic RSS URL returned 404 and was excluded from presets. |
| Optional AI | No live provider/model call was made. Mock transport tests cover real API opt-in, cache, validation and failure behavior. |

## Corrections made during verification

- Corrected conditional 304 handling, URL/encoding edge cases, empty feed content provenance and missing OpenAlex abstract extraction.
- Added malformed-input rejection, strict publication-date display, bounded research citation metadata, missing search filters and explicit practice opt-in.
- Fixed browser assertions that raced navigation and an ambiguous source selector. Added user preferences, date filters, passage reveal and paper-abstract assertions.
- Corrected the restore script's upload response field before its successful rerun. The initial test failure did not indicate lost document data.
- Resolved dependency audit findings with patched packages and used Next's webpack build after a sandbox-specific Turbopack process failure.
- Anchored the private `/data/` gitignore entry so Windows case-insensitive matching cannot accidentally omit `apps/api/Data` and its migrations.

## Remaining limits

- Earlier versions did not retain every source association or overwritten source text. The upgrade preserves what is stored and recovers known provider references; it cannot reconstruct evidence that was already discarded. Reimport/refresh can add available real metadata. Downgrading requires restoring a pre-upgrade database/uploads backup.
- OpenAlex's initial watch seed is a finite newest page; subsequent windows overlap seven days. Papers indexed later than that may require explicit search, and cursor continuation beyond the first page may require a provider API key.
- No live AI credentials/local model were provided, so live model compatibility is unverified. Some compatible servers require `AI_TOKEN_PARAMETER=max_tokens`.
- OCR, DRM removal, captions/media downloads, Semantic Scholar, authenticated global YouTube/SoundCloud search, OPML import and automatic JSON merge-import are not implemented. Supported alternatives are documented in the README; no stub reports these as available.
- EPUB TOC entries pointing to several fragments in one XHTML document remain one section. PDFs use actual extracted pages/bookmarks and optional manual page ranges; complex layout extraction may be imperfect.
- OpenAlex abstracts/access/citations depend on source availability. No paper PDF is silently downloaded. At most 100 valid citation references are stored and 30 are shown in the reader; provider counts/truncation metadata remain available.
- Daily editions use UTC days. Ranking and filtering target a personal catalog, with in-memory candidate scoring. There is no vector index, automatic knowledge graph inference, grading or verified-mastery claim.
- Existing daily editions remain stable when subscriptions/preferences change; use New selection to apply changes. Source deletion preserves imported records and personal state, so retained records may still be discoverable.
- Internet outages prevent new retrieval and remote players/AI, while existing local text, files, notes, search and trails remain usable. Browser clients still need the local application running; no offline service-worker cache is provided.
- This is a single-user loopback app. Public/LAN deployment, authentication and cross-device sync were deliberately outside the requested local deployment.
