# RabbitHole

A public catalog with a private learning workspace. Visitors browse real published articles, research, videos and music. One administrator follows branching trails, keeps notes, reads private books and revisits ideas. There are no visitor accounts, analytics, streaks or fabricated feed entries.

Public catalog routes are `/`, `/search`, `/items/{id}`. **Blackjack Lab** at `/blackjack` is a separate, public interactive learning feature. Personal features are under `/admin`, with sign-in at `/admin/login`. Public records expose approved metadata and bounded source excerpts. Uploads, full stored text, notes, history, trails, personal practice and AI output stay private. Control visibility with **Show publicly** in item Details and the Library visibility filter. The upgrade publishes eligible existing external records; document-derived records are always private.

Public search and pagination use a sanitized database index maintained transactionally with catalog changes. Its additive upgrade backfills existing records without changing saved/read state or publication choices. Blackjack lesson progress migrates automatically to stable IDs and stays synchronized across browser tabs.

See [public hosting and migration](docs/public-hosting.md) for DigitalOcean and GitHub Actions setup for `rabbithole.quintonloges.me`. Configure SSH through GitHub Actions secrets. Backups default to encrypted storage on the server; off-server storage is optional. Live release requires server/DNS configuration and successful migration/backup gates.

## Desktop environments

Both environments use the existing Docker Desktop installation with Linux containers:

| Environment | Address | Compose project | Data |
|---|---|---|---|
| Personal production | **[http://localhost:47831](http://localhost:47831)** | `rabbithole` | Existing library, original database/uploads volumes |
| Development | **[http://localhost:47832](http://localhost:47832)** | `rabbithole-dev` | Independent database/uploads volumes, initially empty |

Open PowerShell in this repository:

```powershell
./scripts/production.ps1 start   # Starts preserved production images; never builds
./scripts/dev.ps1 start          # Builds this checkout and starts development
docker compose exec api dotnet RabbitHole.Api.dll --admin-password # Interactive setup/recovery
```

Both scripts also accept `status`, `logs`, `stop`, and `restart`. Production is a private deployment snapshot in ignored `.local/production/`, with pinned image IDs and its own Caddyfile. Keep that folder. Development uses the working checkout's `compose.yaml` and `.env`; ordinary `docker compose` commands now target development. Production updates are deliberate; rebuilding development cannot deploy new code to production.

For a fresh development checkout, first copy `.env.example` to `.env` without overwriting existing configuration, then run `docker compose up --build -d --wait`. No host .NET or Node installation is needed for Docker startup. The private production snapshot is desktop-specific and is not in Git.

Only each Caddy gateway publishes a host port, bound to `127.0.0.1`. The database and uploaded files persist across container rebuilds and normal stops. See [environment operations](docs/environments.md) for separation, releases, backups and restore. Protected phone/LAN access is a separate deployment step; it is not enabled here.

## First visit

RabbitHole opens in dark mode by default. Choose **Settings → Appearance** to switch to Light or Follow system; saved preferences take precedence.

1. Provision the administrator password with the interactive command above, visit `/admin/login`, then open **Settings**. Edit the starter topics, select a verified suggested feed or enter your own RSS/Atom/site URL, and add it. Sources are never silently subscribed.
2. Refresh the source. Its actual imported count, last success and error appear alongside persistent jobs. Use **New selection** on Discover to include newly imported items immediately.
3. Open a source, save it, write a note, or choose **Follow the rabbit hole**. Each branch and your current position persist. Questions are visibly user-authored; recommended links always refer to catalog records.
4. Use **Library → Books & files** to import a personal PDF or EPUB, navigate its actual sections and save a reading position. PDF bookmarks and named ranges appear first; individual pages have a separate searchable list. **Rebuild sections** applies improved extraction to an existing book while preserving notes and progress. Scanned/encrypted/unparseable files retain an original-file fallback.
5. **Search** queries the local catalog and your notes. **Search research** requests real provider results; import selected results to keep them. Individual YouTube and SoundCloud URLs can be imported here.
6. **Practice** starts off. Enable it when you choose, pick a source and a prompt kind, write a response, reveal its passage and self-rate. Opening or reading never awards understanding or recall automatically.

The item reader puts the source text, abstract or consented media player first. Books have compact previous/next navigation and a searchable **Contents** dialog. **Notes**, **Explore** and **Details** sit below the content; select source text to highlight it or quote it in a note. Unsaved note drafts stay with their item for the current browser session. PDF text wraps to the reader width; **Details → Preserve PDF line breaks** restores extracted line breaks when layout matters, and **Original file** opens the PDF at the current page.

Fresh installations start empty. Deterministic fixtures live only under `tests/`; they are never inserted into the real database. The existing local instance was explicitly populated in October 2026; see the [feed curation report](docs/feed-curation-report.md) for actual counts, verified sources, editorial findings, and limitations.

## Blackjack Lab

Open **Blackjack Lab** in the navigation or visit [the development lab](http://localhost:47832/blackjack). Learn through seven interactive lessons, drill basic strategy by category, or play complete blackjack rounds with virtual chips and an optional coach. All modes are immediately available. Lessons, exercises, coaching, and the reference chart use classic **Atlantic City blackjack**: eight decks, dealer stands on soft 17 (S17), American peek, double after non-Ace splits, late surrender, 3:2 naturals, and up to four hands with no resplitting aces. Actual casino rules can vary; the guide explains what to check.

Play deals cards in a short sequence, animates hits and splits, flips the dealer’s hole card, and pauses briefly before showing the result. Reduced-motion preferences skip animations and their delays.

No login, API, AI key, or database is needed for the lab. Lesson completion and the coaching preference stay in browser localStorage and can be reset in the footer. Virtual chips and practice counts last for the current visit. Run `npm run test:blackjack` for its independent engine, strategy, and browser suite. See [rules, architecture, and test coverage](docs/blackjack-lab.md).

## Recommendations and exploration

Discover uses a compact, numbered list. Click a headline to open its item; the format sits beside it in plain text. A single metadata line identifies the source or parent book, author, page location and any content limitation, followed by a short supplied excerpt. Original-source, save, mark-read and hide actions sit underneath. **Why this?** opens a small dismissible explanation without moving the rows. On wide screens, saved trails sit beside the feed. Related discoveries, Library and local Search use the same source context and existing light/dark theme.

**Today’s picks** returns to your finite daily selection. **Browse topic** searches that topic across your imported collection, excluding hidden items, with 30 items per page. Newest imports come first; sections within a book follow reading order. Topic and page stay in the URL for refresh and browser Back. Browsing a topic does not replace the daily selection or change your interests.

Discover explains its choices with your actual topic settings and recorded interactions. Curated starting trails stay labeled as curated; opening an item is not reading or understanding. Specific concept matches connect the existing catalog without AI. Use **Details → Concepts & tags** in an item's reader to add your tags or exclude a mistaken match. These tags describe an item without changing your lasting interests.

Rabbit-hole suggestions exclude sources already on that trail. **Include sources already in this trail** allows an intentional revisit; existing branches and navigation remain available. **Why this?** links to the records behind a connection. Broad suggestions are labeled, and a sparse collection can return fewer suggestions. Routine release/announcement patterns receive a small daily ranking reduction; they remain searchable and can surface through your preferences.

See [the recommendation quality report](docs/recommendation-quality-report.md) for the actual seven-path before/after evaluation, known weaknesses, and preservation checks. The upgrade retains the earlier daily edition as history and creates a fresh selection with recorded reasons; subsequent reloads keep that selection stable.

## Optional curated sources and reading pack

With the app running and Python 3 installed, preview or explicitly apply the editable packs:

```powershell
python scripts/apply-source-pack.py
python scripts/apply-source-pack.py --apply
python scripts/import-reading-pack.py
python scripts/import-reading-pack.py --apply
```

The default is a read-only plan against development. Applying [curated-source-pack.json](config/curated-source-pack.json) verifies and refreshes new sources before enabling them. It preserves matching existing subscriptions, topic preferences, and personal state. Applying [curated-reading-pack.json](config/curated-reading-pack.json) imports only missing selected real links/papers through the normal API, recognizing stored provider aliases before requesting an import. Neither pack runs automatically at startup. Failed new sources stay disabled; failed refreshes of existing sources also print the error and return a nonzero exit code without changing their settings.

Use `--only "EarLevel Engineering"` to select one source, or `--apply --refresh` to explicitly refresh matching subscriptions without changing their settings. Both scripts prompt for the administrator password, retain cookies only in memory, and default to `--base http://127.0.0.1:47832/api`. Use `--base https://rabbithole.quintonloges.me/api` explicitly for the hosted library; remote HTTP is refused. The new clients require the authenticated API and do not target the preserved legacy production image. Both accept `--report PATH` and use Python's standard library. Pack edits do not overwrite existing preferences; use **Settings** for that. After imports, choose **New selection** on admin Discover.

## Configuration

The app works without any API key: RSS/Atom, public arXiv, low-volume OpenAlex where the provider permits it, manual links, official video/music embeds, private documents, search, ranking, trails, notes and self-rated practice. Provider access can change; an error remains visible and does not break local reading.

| Variable | Purpose |
|---|---|
| `PORT` | Development loopback gateway port, default `47832`; production's saved manifest uses `47831` |
| `POSTGRES_PASSWORD` | Local database credential; set before the first database initialization. Changing it later requires changing the database user's password too. |
| `OPENALEX_API_KEY` | Optional OpenAlex key for a larger budget and cursor continuation; absent keys use bounded newest-page watch samples |
| `AI_BASE_URL` | Optional OpenAI-compatible base URL ending in `/v1`, for example `https://api.openai.com/v1` |
| `AI_MODEL` | Explicit model name supported by your configured provider |
| `AI_API_KEY` | Server-only provider credential |
| `AI_ALLOW_LOCAL` | `true` for a deliberately configured local compatible model endpoint; an API key may then be empty |
| `AI_TOKEN_PARAMETER` | `max_completion_tokens` by default; `max_tokens` for compatible servers that require it |
| `INGESTION_PAUSED` | Stops provider calls without changing subscriptions; also available in admin Settings |

The table describes development's `.env`. Production retains its effective settings in private `.local/production/compose.json`; changing development's `.env` has no effect on production. After development environment changes: `./scripts/dev.ps1 start`. For production settings, see [environment operations](docs/environments.md). Also enable AI explicitly in that environment's Settings. AI is only called for an action you request. A remote provider receives bounded source passages and your submitted response. Inside Docker, a host model server can be addressed as `http://host.docker.internal:11434/v1` if that server exposes a compatible endpoint. The default model is intentionally unspecified.

AI summaries, comparisons, reading aids and critique are labeled synthesis and carry real source IDs. Insufficient text, invalid output, timeouts, missing configuration and provider errors fail plainly. Original sources and all non-AI features remain available. There is no unrestricted autonomous tool execution or model-generated source import.

## Development and checks

Requirements: .NET 10 SDK, Node.js 24, Python 3 for the source-pack checks, Docker for the production stack. Package versions and the frontend lockfile are checked in.

```powershell
python -m unittest discover -s tests/scripts -v
dotnet restore RabbitHole.slnx
dotnet build RabbitHole.slnx -c Release
dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release
dotnet format RabbitHole.slnx --verify-no-changes
npm --prefix apps/web ci
npm --prefix apps/web run lint
npm --prefix apps/web run typecheck
npm --prefix apps/web run build
npm ci
npx playwright install chromium
npm run test:e2e
./scripts/check-recommendations-postgres.ps1
```

Playwright starts the real Next frontend on `127.0.0.1:3001` and an isolated API on `127.0.0.1:5081`. Its separate test host substitutes only external HTTP responses and uses a new temporary SQLite database/uploads folder. The production project does not reference that host. Provider tests never require third-party availability. Fixture files can be regenerated with `python scripts/make-test-fixtures.py`.

The PostgreSQL recommendation check restores a disposable copy of the running database, applies migrations, runs the same recommendation HTTP tests on PostgreSQL, and removes only that test database on success. Ingestion and AI are disabled in its test host. It does not insert test fixtures into your personal catalog.

For development without PostgreSQL, an explicit **development-only** SQLite database can be used:

```powershell
$env:DATABASE_PROVIDER = 'sqlite'
$env:ConnectionStrings__Default = 'Data Source=rabbithole-dev.db'
$env:Documents__Path = "$PWD/data/dev-uploads"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Security__AllowedOrigins = 'http://127.0.0.1:47834'
dotnet run --project apps/api -- --admin-password
dotnet run --project apps/api --urls http://127.0.0.1:5080
# In another terminal:
$env:NEXT_TELEMETRY_DISABLED = '1'
npm --prefix apps/web run dev -- --port 47834
```

Production uses PostgreSQL and forward migrations. SQLite is for tests/development; an additive schema upgrade preserves older development databases. Both paths transactionally backfill identities, multiple source origins and explicit topic choices before serving traffic. Never point the test host at your personal database.

OpenAPI at `/openapi/v1.json` requires an administrator session. Anonymous health at `/api/healthz` returns minimal database availability. See [architecture](docs/architecture.md), [ingestion and privacy](docs/ingestion-and-privacy.md), [public hosting](docs/public-hosting.md), [provider checks](docs/provider-verification.md) and the [acceptance report](docs/acceptance-report.md) for validation and limitations.

## Backup and restore

Use **Settings → Export your data** for portable JSON notes, highlights, topics, sources, states, trails, practice and source metadata/text. It omits environment secrets and raw original files. This JSON is an interchange export; there is no automatic JSON merge-import. A full recovery uses both database and uploads backups:

```powershell
./scripts/backup.ps1
```

This defaults to **production** and briefly stops its API while copying a PostgreSQL custom-format dump and the uploads directory into an ignored timestamped `backups/` folder. It restores the API's previous running/stopped state. Use `-Environment dev` for a development backup. Keep `.local/production/` (private production configuration) and `.env` (development configuration) separately; credentials are not copied into the backup. Backups contain private book text and personal notes; store them accordingly.

Take a backup before upgrading. The catalog upgrade preserves original IDs and resolves merged duplicate links to a surviving item. Restoring the pre-upgrade database and uploads together is the supported way to return to older code; migration downgrades are intentionally blocked because the old model cannot represent the new provenance and aliases.

To restore **into a fresh separate project** without overwriting the current app:

```powershell
$previousPort = $env:PORT
try {
    $env:PORT = '47833'
    docker compose -p rabbithole-restored -f compose.yaml up -d --wait db
    docker compose -p rabbithole-restored -f compose.yaml cp ./backups/YOUR-TIMESTAMP/database.dump db:/tmp/restore.dump
    docker compose -p rabbithole-restored -f compose.yaml exec -T db pg_restore -U rabbithole -d rabbithole --no-owner /tmp/restore.dump
    docker compose -p rabbithole-restored -f compose.yaml create api
    docker compose -p rabbithole-restored -f compose.yaml cp ./backups/YOUR-TIMESTAMP/uploads/. api:/data/uploads
    docker compose -p rabbithole-restored -f compose.yaml up --build -d --wait
} finally { $env:PORT = $previousPort }
```

Open `http://localhost:47833`. Use a new project name for each restore and an unused port. Do not use `--clean` against an existing personal database. Restore the database and files from the same backup. This builds the current checkout and uses development's `.env` credential for the separate server; the dump does not change PostgreSQL role passwords. Migration history is preserved and newer forward migrations apply on startup. Never use the production manifest for a temporary restore: its volumes deliberately refer to the real personal library.

To verify a backup containing imported items using a temporary isolated installation:

```powershell
./scripts/verify-restore.ps1 -BackupFolder ./backups/YOUR-TIMESTAMP
```

This uses production's exact preserved images on port `47833` with separate temporary volumes. It disables source ingestion in that copy, checks PostgreSQL search, state/trail persistence and a real PDF across restart, then removes only its uniquely named verification containers and volumes. Both installed environments and the backup are preserved.

## Current limits

- Daily editions roll over at midnight UTC; publication dates are displayed in UTC so date-only research records retain their source calendar day.
- EPUB sections follow spine order and actual EPUB 3 navigation/EPUB 2 NCX anchors, including several chapters within one XHTML file. Unlabelled text retains a heading or filename label; missing anchors or malformed markup fall back to the file's text with a warning. Image-only sections remain in the original. PDF bookmarks define chapters when present; otherwise use individual pages or mark your own ranges. There is no OCR or DRM removal.
- OpenAlex/arXiv supply available metadata and abstracts. An access link does not mean a paper's full text was retrieved. Provider citation references link out; they do not automatically import other papers.
  OpenAlex watches search titles/abstracts, or titles only with a `title:` prefix. They seed a bounded newest page from the last two years, then revisit a seven-day overlap. Without `OPENALEX_API_KEY`, each refresh samples only the first page and can miss papers in busy queries. With a key, fixed-window cursor continuation persists across restarts; failed pages retain progress and report errors. Interactive search remains relevance ranked. Papers indexed more than seven days late may require manual search.
- YouTube channel feeds and known video URLs, and SoundCloud track/playlist URLs, are supported. Authenticated global search, captions, media downloads, Semantic Scholar and OPML import are not implemented.
- AI has fixture validation and failure coverage, but no live model was tested because no credentials or local model were supplied. Playback availability also remains subject to each media provider; live oEmbed metadata was checked, not uninterrupted playback.
- The ranking/filter pipeline targets a personal catalog and loads candidates into memory. It has no vector index or automatic correctness grading. Changing source/topic preferences takes effect in a new selection; existing editions remain stable.
- JSON export has no automatic merge-import. Use the tested database/files restore for recovery.

## Troubleshooting and deliberate reset

```powershell
./scripts/production.ps1 status
./scripts/production.ps1 logs
./scripts/production.ps1 restart
./scripts/dev.ps1 status
./scripts/dev.ps1 logs
./scripts/dev.ps1 stop     # Stops development only; keeps its data
```

To change development's port, edit `PORT` in `.env` and run `./scripts/dev.ps1 start`. Production's port is `services.gateway.ports[0].published` in its private manifest; after an intentional change, run `./scripts/production.ps1 start`. Keep both bindings on `127.0.0.1` and ports distinct. Provider errors are in Settings; retry after backoff or correct the source. Uploaded files without extracted text remain downloadable; OCR and DRM removal are not provided. For an interrupted or failed upload, use **Library → Retry extraction**, or upload the same file again. For a ready book with old section boundaries, choose **Rebuild sections**. Existing IDs, notes, edited metadata and reading progress are retained; replaced sections stay under **Previous extraction** with their original text. Reuploading the same file also restores a missing original.

**Destructive development reset:** only after deciding to erase development, `docker compose -p rabbithole-dev --env-file .env -f compose.yaml down --volumes` deletes its database and uploads. Never use it for routine rebuilds or troubleshooting. Production volumes are external and are not removed by Compose; deleting them manually would erase the personal library.

Desktop Compose remains loopback-only. The current app has a public catalog and a password-protected private workspace; use the separate hosted configuration for HTTPS internet access. Explicit `Security:AllowedHosts`, `Security:AllowedOrigins` and exact `Security:TrustedProxies` replace broad remote-host exceptions. Provision/recover the password interactively; do not weaken these boundaries to resolve a configuration error.
