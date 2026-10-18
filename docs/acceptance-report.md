# RabbitHole acceptance report

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
