# RabbitHole acceptance report

## Public catalog and private administrator migration — 24 October 2026

Implemented the public SSR catalog/search/item pages, separate public DTO/API contracts, persistent publication controls, private `/admin` workspace, password-only administrator, persisted/revocable cookie sessions, CSRF on writes, explicit host/proxy trust, rate limits, ingestion pause and additive PostgreSQL/SQLite upgrades. Public eligibility covers document references and alias components, local-upload/provenance signals, nested credential/private URLs, bounded excerpts and approved media. Existing personal workflows remain under `/admin`.

Added separate hosted/staging Compose and Caddy configurations, versioned image transfer/pinning through GitHub Actions, interactive account recovery, consistent backup/preservation manifests, encrypted off-server backup/restore commands, nightly retention/timer and release-specific launch gates. Initial deployment deliberately stops before creating empty volumes so the personal library can be restored first. Later upgrades back up the previous release and compare original records/files after migration. A full DigitalOcean launch is **not complete**: SSH access, configured off-server destinations and DNS/ACME validation are still required. The original local production instance remains running with its preserved images/volumes; it was not upgraded or frozen for final cutover.

Actual verification:

- `./scripts/check.ps1`: **10 maintenance/source-pack tests, 5 launch-evidence tests and 240 backend tests passed**; the two PostgreSQL-only tests explicitly skipped in this SQLite run. Dependency install, ESLint, clean route generation/TypeScript and optimized Next standalone build passed. `dotnet format RabbitHole.slnx --verify-no-changes --no-restore` passed after formatting the new files. Local .NET test/formatter and Docker checks required access to Windows named pipes outside the sandbox; early pipe failures were infrastructure failures and were rerun successfully.
- `./scripts/check-recommendations-postgres.ps1`: **5 passed, zero skipped** against a disposable copy of development's PostgreSQL 17 database. This includes the two hosted migration/publication/authentication checks, legacy backfill-before-merge, the single-account database constraint, real PostgreSQL search/filter/counts and persisted sessions/visibility after application restart. The disposable database was removed.
- Playwright: **33/33 passed**, covering all retained admin journeys and new public/authentication/publication flows. The public subset subsequently passed **6/6** after compacting mobile filters and tightening navigation assertions. Checks include SSR/HTML privacy, public filters/pagination/search, document publication rejection, login/logout/CSRF, media click-to-load and no personal-state writes during anonymous browsing. Browser tests revealed and fixed hydration timing for sign-out and pending publication-toggle state. Desktop/mobile screenshots were reviewed in `.cache/ui-review/`; no horizontal overflow was observed.
- `./scripts/dev.ps1 start`: built the API and optimized frontend, migrated development and started all four services healthy on loopback `47832`. Development uses separate database/uploads/auth-key volumes. This did not rebuild or replace the original `rabbithole` deployment.
- Personal backup `backups/20261024-001836-production` captured **781 real items**, all original tables and both uploaded originals. The first manifest command encountered Windows default-text decoding; explicit UTF-8 fixed it. The manifest was then generated from the restored, unchanged dump and verified through the complete upgrade, not inferred from live mutable data.
- `./scripts/rehearse-migration.ps1 -BackupFolder ./backups/20261024-001836-production`: **passed** in project `rabbithole-migrate-20261024002056`. All original-column hashes across **19 tables** and **2 uploaded-file hashes** matched before upgrade, after upgrade and after database/API/web restart. **346 public external items** were served; **435 document-derived items** were excluded from all paginated public IDs, private detail samples returned 404, legacy private APIs/OpenAPI returned 401. Subscriptions, notes, saved/read/progress state, branches, practice and stable IDs participated in the preservation hashes. The new schema and upgrade markers were additive. Rehearsal containers/volumes were removed; ignored evidence is in `.cache/latest-migration-rehearsal.json` and matching manifest files.
- `./scripts/verify-restore.ps1 -BackupFolder ./backups/20261024-001836-production`: **passed** with the original preserved images in a separate rollback project. It restored all 781 items, exercised PostgreSQL note search, saved/read/progress state, sibling trail branches/navigation, Linux PDF parsing, original-file hashes and export across full restart. An app interruption left the first temporary attempt running; only that named test project's resources were cleaned up, then the full check was rerun successfully and cleaned up.
- Hosted Compose + staging override started all services with pinned local images, PostgreSQL **17.11**, internal TLS and only loopback gateway ports. **22 HTTPS checks passed**: correct origin/path redirect, healthy/public SSR/public API, anonymous API/OpenAPI denial and admin redirect, CSRF/cross-site rejection, genuine interactive administrator provisioning and login, Secure/HttpOnly/SameSite=Lax/host-only cookies, protected settings, forged forwarding protection, unchanged session expiry after forced container replacement, logout and subsequent 401. TLS was checked against Caddy's private CA. The test identified and fixed an API/gateway IP-allocation collision and a staging tunnel-origin mismatch. Only its generated containers/networks/five volumes were removed. Evidence: `.cache/hosted-verify/evidence.txt` and `https-results.txt`. These transport tests used the earlier locally built images; the final source additionally has stricter nested-URL/document checks verified in the backend suite.
- Hosted Bash syntax, both Caddy configurations, Python compilation and `actionlint 1.7.7` passed. Launch-evidence tests confirm that an initial import marker, an old backup or mismatched snapshots/release cannot open the site. Private content/recovery configuration must have independent encrypted off-server repositories; the restore gate downloads and restores the actual recorded pair in isolated volumes.
- One bounded, read-only live-provider probe of configured **EarLevel Engineering** parsed **10 real RSS entries** from `https://www.earlevel.com/main/feed/`. It did not subscribe/import or mutate personal data. No provider interface was changed and no live AI/model call was attempted.

Remaining operational prerequisites: configure DigitalOcean SSH/DNS and GitHub environment secrets/variables, configure and initialize both off-server backup repositories, run the actual encrypted backup/restoration on that server, provision the real admin password interactively, take the final desktop `-KeepStopped` backup, transfer it securely and run the HTTPS/authenticated launch gate. Public ACME issuance, actual GitHub runner deployment and off-server restoration are not claimed as passed. Public metadata filtering currently scans projected candidates in memory and has not been load-tested for a large public audience. Exact setup/release/recovery/rollback commands are in [public-hosting.md](public-hosting.md).

## Interface copy edit — 23 October 2026

Rewrote the interface copy across Discover, Rabbit Holes, Library, Practice, Search, Settings, and the reader. Removed the repeated slogans and decorative headings; shortened instructions, empty states, placeholders, recommendation explanations, and AI error messages. Buttons and field labels now name their actions directly. Source tests display a readable count instead of raw JSON, and a library filter with no matches has its own empty-state message. Source excerpts, uploaded text, stored user content, ranking calculations, and database schema were not edited.

Actual verification:

- `./scripts/check.ps1`: **7 script tests and 194 backend tests passed**, followed by dependency installation, ESLint, TypeScript, and the optimized frontend build. The initial sandbox run could not connect to the .NET test host; the run with local process access passed.
- `dotnet format RabbitHole.slnx --verify-no-changes --no-restore`: passed.
- `$env:CI = '1'; npm run test:e2e -- --reporter=line`: **25 passed**; two assertions still expected the old interface text. Updated those selectors, then `npm run test:e2e -- --last-failed --reporter=line`: **2 passed in 13.4 seconds**. All 27 checks passed across these runs. Coverage includes persisted saved/read state, notes, trails, practice answers, imports, failure recovery, and all six main routes at 390px. Reader and discovery layouts also passed at 1024px and 1440px.
- `./scripts/dev.ps1 start`: rebuilt the API and optimized web app and started all four development services successfully. Docker required access to its local named pipe. Rebuilt once more after a final sentence edit in Settings.
- Reviewed all six main pages and the reader in the in-app browser against development. Inspected the mobile discovery and Settings screenshots from the fixture tests. The longer labels fit without horizontal overflow. Recent API/web logs contained no application errors and reported no pending migrations. `git diff --check`: passed.

Running in **development only** at `http://localhost:47832`. Production was not deployed. No live provider or AI calls were needed for this copy edit; integration and failure checks used the existing isolated fixtures. Previously saved recommendation explanations retain their original text until a new selection is requested.

## Item-page UX review and revision — 23 October 2026

An independent agent reviewed the actual PDF item page, the implementation and desktop/mobile screenshots. It found that source text started more than two screens down, expanded contents and equal-weight management actions dominated reading, excerpts repeated the body, and item navigation could carry drafts or delayed results into another item. The revised page was reviewed again; no remaining visual blocker was found.

- The parent title/page, plain source metadata, Save/Mark read, secondary More actions and original link precede a compact Previous/Contents/Next bar. The full searchable contents list lives in a bounded dialog, with Escape/outside/close dismissal and focus restoration. Navigation repeats after the text.
- Source text or the consented media player comes before Notes/Explore/Details. Abstracts are labeled; duplicate excerpts are omitted; link-only and extraction failures explain the available fallback. Notes, related discoveries, trails, AI tools, metadata, topics and concepts remain connected to their existing endpoints.
- PDF presentation removes extraction tabs and reflows physical line wraps while preserving every word, hyphen and existing blank-line paragraph boundary. Stored source text is unchanged. Details offers original line breaks, remembered for that document during the browser session; the original PDF link includes the actual page. Extraction without blank paragraphs remains a continuous paragraph rather than inventing structure.
- Each item has its own reader session and browser-session note draft. Late AI/note responses cannot populate a different item. Highlighting accepts source-only selections, reports saved/error states locally and does not duplicate a quoted draft. Quote-in-note is disabled while a note save is pending. Read, understood and per-item progress stay separate.

Actual verification:

- `npm --prefix apps/web run lint` and `npm --prefix apps/web run typecheck`: passed.
- `$env:CI = '1'; npm run test:e2e -- --reporter=line`: **26 passed**, with one new test assertion incorrectly treating a trailing space as changed source text. After correcting that assertion, `npm run test:e2e -- --last-failed --reporter=line`: **1 passed in 7.5 seconds**. All 27 checks passed across those final runs. New coverage includes 1440/1024/390px layout, navigation failure/retry, contents search and keyboard dismissal, PDF reflow/original-line preference across pages, item-specific drafts, failed/pending note saves, source-only highlights, consented media, abstracts/link-only items and a delayed AI response across navigation. Existing real-backend journeys verified persisted notes, reading state, EPUB/PDF progress, metadata, trails and practice. Earlier runs exposed a search-input Escape handling issue and outdated selectors for collapsed controls; these were fixed before the final checks.
- `./scripts/dev.ps1 start`: rebuilt the optimized Next frontend and started the development stack successfully; API, web, gateway and database were healthy. The initial sandbox attempt could not access Docker's named pipe; the local-process run passed. A sandbox E2E startup that produced no test output was stopped; the isolated local-process run above completed.
- Inspected the rebuilt real PDF page and Contents/Details/Notes interactions in the in-app browser. The text begins at **y359**, compared with **y2575** before the revision at the same desktop viewport. Browser error logs were empty. Inspected the light-theme fixture screenshots at 1440px and 390px; layout checks also covered 1024px, with no horizontal overflow. Actual dark-theme screenshots are in ignored `.cache/ux-review/item-final.png`, `item-contents-final.png` and `item-tools-final.png`; fixture screenshots are `test-results/item-reader-*.png`.
- `docker compose logs --since 10m --tail 80 api web`: no application errors; no pending database migrations. `git diff --check`: passed. Backend logic and schema were not changed in this revision; previous backend results remain recorded below.

Running in **development only** at `http://localhost:47832`. Test mutations and fixtures used isolated servers. No production deployment, new source import or live AI/provider request was part of this revision.

## Denser rows and plain metadata — 23 October 2026

Removed the routine “Read here”/player-availability labels and the duplicate reader action. Headlines open items. Formats now appear as plain text beside the headline; source, author, page location and useful limitations share one metadata line. Removed the discovery eyebrow heading and row badges. Excerpts use one line on desktop and two on mobile, with tighter row padding and controls. Related, Library and Search rows use the same compact presentation.

- `npm run lint` and `npm run typecheck` in `apps/web`: passed.
- `$env:CI = '1'; npm run test:e2e -- --reporter=line`: **22 passed in 54.4 seconds**. Updated existing mixed-content expectations; topic navigation, popover/focus behavior, saved state, reader/document flows and mobile checks passed.
- `./scripts/dev.ps1 start`: optimized frontend build and development startup passed; all development services started successfully.
- Inspected the actual dark-theme daily feed and light-theme mixed-content desktop/mobile screenshots. At the same **1210 × 1062** browser viewport, the first six real rows decreased from **189px to 105px each** (44% shorter), with no horizontal overflow or “Read here” text. Final actual-library screenshot: ignored `.cache/ux-review/compact-feed-final.png`.
- `git diff --check`: passed. Backend logic and schema were not changed in this follow-up; the preceding backend results remain recorded below.

Running in **development only** at `http://localhost:47832`.

## Discovery UX counter-review and revision — 23 October 2026

An independent counter-review identified three problems in the first list design: topic tabs only filtered the small daily edition, paired disclosures hid essential row context and moved the list, and document rows mislabeled pages as chapters without identifying their parent book. The revision addresses those findings while retaining the theme and numbered list.

- **Topic navigation:** separate Today’s picks and Browse topic controls. Topic browsing uses the whole imported collection, excludes hidden items before counting/pagination, and preserves topic/page in the URL. New imports come first; each book's sections stay in reading order. Returning to the daily edition preserves its selection and evidence.
- **Immediate row context:** accurate PDF page/section, EPUB section, document, research, video and audio labels; actual text/player/original-only availability; source or parent book, author, page bounds and a visible source excerpt. Generic page and parser-filename headings display the real parent and page/section position. Local uploads no longer show an irrelevant unknown publication date or the internal provider name.
- **Interactions:** removed Preview; Why this? opens a bounded native popover without expanding the row, supports Escape/outside/close dismissal, and closes when the page scrolls. Read/open actions describe the content being opened. Library, local Search and related rows reuse the same context helpers.
- **API projection:** batched parent/section metadata plus an explicit `hasText` flag, even when list payloads omit the body. No schema migration, text synthesis or private storage-path exposure was introduced.

Actual verification:

- `npm run lint` and `npm run typecheck` in `apps/web`: passed.
- `dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release --no-restore -v:minimal`: **194 passed**. New coverage checks parent metadata, page/section distinctions, missing text, private-field omission, reading-order pagination, hidden-item filtering and stable daily editions.
- `dotnet format RabbitHole.slnx --verify-no-changes --no-restore`: passed. The sandbox initially denied the build-host pipe; the same check passed with local process access.
- `$env:CI = '1'; npm run test:e2e -- --reporter=line`: **22 passed in 58.6 seconds**. Added mixed-format rows and topic navigation tests cover visible excerpts, truthful availability, no row movement, Escape/focus behavior, scroll dismissal, mobile popover bounds, pagination, reload, browser Back, empty topics and returning to the same daily picks.
- `./scripts/dev.ps1 start`: rebuilt the API and optimized Next frontend and started development successfully. API, web and database were healthy; gateway remained bound to `127.0.0.1:47832`. Recent API/web logs contained no application errors; no migrations were pending.
- Inspected the rebuilt app in the in-app browser with the actual PDF/EPUB library: daily rows, topic rows in reading order, parent/author/page context, and explanation open/close. Final screenshots are in ignored `.cache/ux-review/live-dark-final.png`, `live-topic-final.png` and `live-popover-final.png`. Inspected the isolated mixed-type fixture screenshots at desktop and mobile; E2E also checked 1440, 1024 and 390px for horizontal overflow. Test screenshots are in `test-results/discovery-context-*.png`.
- `git diff --check`: passed. Preexisting checkout work was preserved.

Running in **development only** at `http://localhost:47832`. Fixtures and state-changing checks used isolated test servers. No production deployment or new live-provider smoke request was part of this UI revision. Source excerpts remain the supplied text; missing chapter names or abstracts are not invented.

## Compact discovery layout — 23 October 2026

Replaced the oversized featured item and two-column discovery cards with a single numbered list inspired by Hacker News and old Reddit. Each row keeps the headline, original source domain, source-reported date/author/type, and inline save/read/pass actions. Supplied excerpts and recommendation evidence expand through keyboard-accessible **Preview** and **Why this?** disclosures. Topic filters use compact tabs; resumable trails sit beside the feed on wide screens. Related discoveries share the list component, and Library/local Search rows use the matching headline/metadata treatment. Existing theme colors, reading typography, navigation, state endpoints and ranking remain in place.

Actual verification:

- `./scripts/check.ps1` passed: **7 script tests**, **185 backend tests**, frontend dependency installation, ESLint, TypeScript, and optimized Next build. Backend compilation succeeded as part of the Release test run.
- `dotnet format RabbitHole.slnx --verify-no-changes --no-restore` passed.
- `CI=1 npm run test:e2e -- --reporter=line` passed **20 tests** in 48.7 seconds, including source imports, persistent save/read state, branching, document navigation, practice, failure recovery, and mobile navigation.
- `./scripts/dev.ps1 start` rebuilt and started the development stack successfully at `http://localhost:47832`. All services were healthy. Recent API/web logs showed no application errors; the database reported no pending migrations.
- `node .cache/desktop-layout/review.cjs` passed against the production-built development frontend with a separately spawned, temporary fixture API. Eight checks covered **1440, 1024, 720 and 390px** in **light and dark** themes: single-column rows, no horizontal overflow, and no browser exceptions. Inline save/read/pass survived reload; opening/reading did not mark understanding. Original-source links, keyboard disclosures, topic selection, surprise, related routes, Search, API-error retry and empty states were checked. The first review run stopped on an ambiguous test selector matching Next's hidden route announcer; narrowing it to the application's error alert completed the review without an application change.
- Inspected desktop, expanded-details, mobile-dark, and Search screenshots. Review images and the actual result JSON are in ignored `.cache/desktop-layout/`; the standard E2E desktop screenshot is in `test-results/desktop-discover.png`. These previews contain explicitly labeled test fixtures. The development catalog was identical before and after review; fixtures and mutations went only to the temporary test API.
- `git diff --check` passed. Existing unrelated work in the checkout was preserved.

This change is running in **development only**. No production release, provider integration, external-source request, database migration, or AI behavior change was part of this layout work.

## Production promotion of document fixes — 19 October 2026

Deployed the already tested development API/web images to the personal production instance at `http://localhost:47831` on the owner's explicit request. The owner also explicitly waived the backup for this release; **no database, uploads, export or deployment-manifest backup was taken**. Verification snapshots were held in memory and only counts/check results were retained.

- Promoted API image `sha256:1e92aa88250c8b98adf2e03a0b138aff6a9d5694c8607e875be339b0bb469f6a` and web image `sha256:7fe61e5581a0f70e254a9aaa7582885832652cd4f35f6d3f31d08f6cea976f06`, retaining unique local production release tags. Only the two image fields in the private manifest changed; provider settings, database/uploads volumes, network, loopback port and database/gateway containers were preserved.
- `./scripts/production.ps1 start` passed with all services healthy. The additive section-history migration applied successfully. Pre/post deployment checksums matched for documents, original sections, notes, saved/read/progress state, trails, practice records and settings; every prior item ID remained.
- Rebuilt the existing affected EPUB through its production retry endpoint. It now has **26 active sections and six preserved previous extractions**. All existing section/item references remain; superseded sections retain their original text. Personal-state checksums still match after rebuilding. Both documents retain their original metadata/file identities.
- Counts changed from **716 to 728 items** and **423 to 435 sections**, reflecting 12 newly separated EPUB sections while retaining old records. The **two documents, 13 personal-state records, two trails and zero notes** were preserved.
- `node .cache/document-repair/verify-production-ui.mjs` passed **four book/viewport checks** at 1440px and 390px: clean text encoding, 34 PDF bookmark ranges before the separate page list, corrected EPUB navigation and previous-extraction access, no horizontal overflow, no browser/API errors and no UI state mutations. Production screenshots and validation summaries remain under ignored `.cache/document-repair/`.
- Recent production logs contained zero application failure-pattern matches. The private deployment audit under `.local/production/deployments/` records image IDs, backup waiver, counts and preservation checks without copying library contents or secrets.

## Library encoding and document sections — 19 October 2026

Corrected the library's corrupted UTF-8 interface literals. PDF bookmark/manual ranges now precede a separate searchable, paginated page list; original-file access, rebuild and range controls remain accessible. EPUB navigation uses actual EPUB 3/NCX fragment targets in spine/document order, including multiple chapters in one file. Missing/malformed TOC links and markup retain readable text with an explicit fallback; image-only entries remain in the original. Ready documents expose **Rebuild sections**.

The additive `DocumentSectionHistory` migration and equivalent SQLite upgrade retain old section IDs and text under **Previous extraction** when chapter boundaries change. Notes, saved/read/progress state, owner-edited titles and trail references remain on their original records. Current source labels and reading order refresh without duplicating sections. Section responses now return compact metadata rather than copying entire book bodies back into the library.

Actual verification:

- `dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release --no-restore -v:minimal --blame-hang-timeout 60s` — **185 passed**, zero failed/skipped. Includes EPUB fragment/nested/duplicate TOC tests, malformed navigation, local entity handling, encoded filename collisions, and rebuild preservation of notes/state/trails/titles. The final build had no compiler warnings.
- `dotnet format RabbitHole.slnx --verify-no-changes --no-restore` and `dotnet ef migrations has-pending-model-changes --project apps/api/RabbitHole.Api.csproj --no-build` — passed; no pending model changes.
- `npm --prefix apps/web run lint` and `npm --prefix apps/web run typecheck` — passed. `./scripts/dev.ps1 start` built the Release API and optimized Next frontend and started all four development services successfully.
- `CI=1 npm run test:e2e -- --reporter=line` — **20 passed** in 46.6 seconds. Five new UI regressions cover large PDF navigation, clean encoding, loading/failure/rebuild states, reupload cache refresh, chapter/page navigation, preserved extractions and EPUB order. External network responses remain isolated deterministic fixtures.
- `python -m unittest discover -s tests/scripts -v` — **7 passed**. `git diff --check` passed.
- Read-only copies of the two reported production originals were placed in ignored `.cache/document-repair/` and imported into **development only**. The PDF has **369 pages and 34 genuine bookmark ranges**; those ranges were previously buried after the page records. The EPUB now has **26 nonempty sections**, including its previously merged first chapter. All text from its 19 readable spine files is preserved without duplication when whitespace is normalized. No warnings or duplicate locators were produced for either file.
- `node .cache/document-repair/verify-live.mjs` passed against development: same-hash reuploads reuse the same documents, repeated rebuilds preserve section IDs/order, original-file downloads are byte-identical, and actual section reading endpoints work. A second pass after container recreation matched the prior section snapshot exactly.
- `node .cache/document-repair/verify-ui.mjs` passed: all six main routes at 1440px and 390px (**12/12 HTTP 200**, no horizontal overflow, browser exceptions or failed local responses), plus actual PDF/EPUB section reading and next-section navigation. Dark desktop/mobile screenshots are in `.cache/document-repair/live-*.png`; library layouts were visually inspected. Deterministic UI screenshots are in `test-results/document-sections-*.png`.
- Development health checks passed and recent logs contained zero application failure-pattern matches. Production's preserved containers remain healthy on `127.0.0.1:47831`; they were not rebuilt, restarted or deployed. Development is available at `http://localhost:47832`.

Local .NET build-host pipes, test-host sockets, browser loopback access and Docker required execution outside the default sandbox; those checks completed with the required access. No external provider or AI behavior changed. Untitled EPUB continuation text still uses an honest heading/filename fallback; no missing chapter title is invented. PDFs without bookmarks require individual pages or user-defined ranges, and OCR/DRM removal remain unsupported. Production deployment requires a separate explicit request and backup per `docs/environments.md`.

## Desktop environment separation — 18 October 2026

The original `rabbithole` Docker project is now personal production at `http://localhost:47831`. All four running image IDs were preserved; no application upgrade or database migration was introduced. The original `rabbithole_database` and `rabbithole_uploads` volumes remain attached and are now declared external. Private runtime settings and a separate Caddyfile are captured under ignored `.local/production/`, with immutable image IDs, no build contexts and `pull_policy: never`.

The working checkout defaults to independent `rabbithole-dev` on `http://localhost:47832`, with different database credentials, image names, network and database/uploads volumes. It has no imported items, subscriptions or copied provider keys. Scripts now explicitly select the intended environment; ordinary builds, pack scripts and PostgreSQL recommendation checks target development. Backups default to production. Both gateways remain loopback-only; host port 3000 is free and no LAN/firewall configuration was changed.

Actual verification:

- Took a complete pre-change database/uploads backup at `backups/20261018-010644`. Captured a private API export baseline and retained original deployment metadata locally.
- `./scripts/production.ps1 start` and `./scripts/dev.ps1 start` passed. Both full stacks are running; database, API and web health checks pass, and both gateway health requests return `ok`.
- Exact pre/post export comparisons passed across every exported collection except the intentionally changing export timestamp: **276 items, 35 sources, 8 topics, 9 personal-state rows, 2 trails, 10 nodes, 628 identities, 259 item origins, 2,208 item-topic rows, 3 editions and 53 edition entries**, with notes/documents/sections/practice collections remaining empty. The final comparison after all checks also passed.
- `./scripts/check-environments.ps1` passed: distinct live containers, networks, database/uploads volumes, loopback ports and pinned production images; temporary development topic/branches/navigation persisted across restart; production containers and personal state were unaffected. Temporary development records were removed.
- Updated `./scripts/backup.ps1` passed against production, creating `backups/20261018-011521-production`. Its dump/files copy and API resume completed successfully.
- `./scripts/verify-restore.ps1 -BackupFolder ./backups/20261018-010644` passed on temporary port 47833 using production's exact images. Restored all **276 items**, disabled ingestion only in the copy, verified PostgreSQL note search, saved/read/progress, sibling branches, PDF parsing/original-file hash/export and database/API/web restart. Only the temporary verification containers, network and volumes were removed.
- `./scripts/check-recommendations-postgres.ps1` passed **2 HTTP tests** against a disposable database on the **development** PostgreSQL server. That test database was removed.
- `python -m unittest discover -s tests/scripts -v`: **7 passed**. Backend Release build passed with zero warnings/errors; **159 backend tests passed** and `dotnet format RabbitHole.slnx --verify-no-changes --no-restore` passed. The sandbox initially prevented the host backend build; the same checks passed with runtime access outside that sandbox.
- Frontend lint and TypeScript checks passed. The Docker development build completed a production Next build. `CI=1 npm run test:e2e -- --reporter=line`: **15 passed** in 41.6 seconds on the isolated fixture host.
- Live Playwright checks visited Discover, Rabbit Holes, Library, Practice, Search and Settings in both environments at 1440px and 390px: **24/24 route responses were HTTP 200**, with no browser exceptions or failed local requests after the backup pause. Screenshots and results are under `.cache/environment-ui/`. The first pass overlapped the intentional API backup pause and saw two transient 502s; the post-backup pass had none.
- PowerShell parsing, ignored-private-file checks and `git diff --check` passed. Final live counts are **276 production items / 0 development items**. Listening-port checks confirmed only `127.0.0.1:47831` and `127.0.0.1:47832` for these deployments, with no listener on 3000 or the temporary restore port 47833.

Known existing UI issue: production Settings with the populated source/job list has horizontal overflow at 390px (the full-page screenshot is 706px wide); the other 23 route/viewport checks fit. This environment-only change preserves the existing production application version. The layout issue is recorded for the subsequent phone-access work. No new provider integration or AI behavior was introduced, so no additional external provider calls were required. Docker Desktop startup after an OS reboot was not exercised; both stacks retain their `unless-stopped` policies.

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
