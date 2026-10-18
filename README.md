# RabbitHole

A private discovery feed with a memory. Follow real articles, research, books, videos and music into branching trails; keep notes and revisit ideas when you choose. There are no accounts, analytics, streaks or fabricated feed entries.

## Start on Windows

Install Docker Desktop with Linux containers, then open PowerShell in this repository:

```powershell
Copy-Item .env.example .env   # Only on first setup; do not overwrite an existing .env
docker compose up --build -d
```

Open **[http://localhost:3000](http://localhost:3000)**. Only Caddy publishes a host port, bound to `127.0.0.1`. PostgreSQL and uploaded documents live in named Docker volumes. Rebuilding or `docker compose down` preserves them.

On macOS/Linux, use `cp .env.example .env` before the same Compose command. No host .NET or Node installation is needed for Docker startup.

## First visit

1. Open **Settings**. Edit the starter topics, select a verified suggested feed or enter your own RSS/Atom/site URL, and add it. Sources are never silently subscribed.
2. Refresh the source. Its actual imported count, last success and error appear alongside persistent jobs. Use **New selection** on Discover to include newly imported items immediately.
3. Open a source, save it, write a note, or choose **Follow the rabbit hole**. Each branch and your current position persist. Questions are visibly user-authored; recommended links always refer to catalog records.
4. Use **Library → Books & files** to import a personal PDF or EPUB, navigate its actual sections and save a reading position. Scanned/encrypted/unparseable files retain an original-file fallback.
5. **Search** queries the local catalog and your notes. **Search research** requests real provider results; import selected results to keep them. Individual YouTube and SoundCloud URLs can be imported here.
6. **Practice** starts off. Enable it when you choose, pick a source and a prompt kind, write a response, reveal its passage and self-rate. Opening or reading never awards understanding or recall automatically.

The initial collection is empty. Deterministic fixtures live only under `tests/`; they are never inserted into the real database.

## Configuration

The app works without any API key: RSS/Atom, public arXiv, low-volume OpenAlex where the provider permits it, manual links, official video/music embeds, private documents, search, ranking, trails, notes and self-rated practice. Provider access can change; an error remains visible and does not break local reading.

| Variable | Purpose |
|---|---|
| `PORT` | Loopback gateway port, default `3000` |
| `POSTGRES_PASSWORD` | Local database credential; set before the first database initialization. Changing it later requires changing the database user's password too. |
| `OPENALEX_API_KEY` | Optional OpenAlex account key for an authorized request budget |
| `AI_BASE_URL` | Optional OpenAI-compatible base URL ending in `/v1`, for example `https://api.openai.com/v1` |
| `AI_MODEL` | Explicit model name supported by your configured provider |
| `AI_API_KEY` | Server-only provider credential |
| `AI_ALLOW_LOCAL` | `true` for a deliberately configured local compatible model endpoint; an API key may then be empty |
| `AI_TOKEN_PARAMETER` | `max_completion_tokens` by default; `max_tokens` for compatible servers that require it |

After environment changes: `docker compose up -d --force-recreate api`. Also enable AI explicitly in Settings. AI is only called for an action you request. A remote provider receives bounded source passages and your submitted response. Inside Docker, a host model server can be addressed as `http://host.docker.internal:11434/v1` if that server exposes a compatible endpoint. The default model is intentionally unspecified.

AI summaries, comparisons, reading aids and critique are labeled synthesis and carry real source IDs. Insufficient text, invalid output, timeouts, missing configuration and provider errors fail plainly. Original sources and all non-AI features remain available. There is no unrestricted autonomous tool execution or model-generated source import.

## Development and checks

Requirements: .NET 10 SDK, Node.js 24, Docker for the production stack. Package versions and the frontend lockfile are checked in.

```powershell
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
```

Playwright starts the real Next frontend on `127.0.0.1:3001` and an isolated API on `127.0.0.1:5081`. Its separate test host substitutes only external HTTP responses and uses a new temporary SQLite database/uploads folder. The production project does not reference that host. Provider tests never require third-party availability. Fixture files can be regenerated with `python scripts/make-test-fixtures.py`.

For development without PostgreSQL, an explicit **development-only** SQLite database can be used:

```powershell
$env:DATABASE_PROVIDER = 'sqlite'
$env:ConnectionStrings__Default = 'Data Source=rabbithole-dev.db'
$env:Documents__Path = "$PWD/data/dev-uploads"
$env:RABBITHOLE_ALLOWED_ORIGIN = 'http://127.0.0.1:3000'
dotnet run --project apps/api --urls http://127.0.0.1:5080
# In another terminal:
$env:NEXT_TELEMETRY_DISABLED = '1'
npm --prefix apps/web run dev
```

Production uses PostgreSQL and forward migrations. SQLite is for tests/development; an additive schema upgrade preserves older development databases. Both paths transactionally backfill identities, multiple source origins and explicit topic choices before serving traffic. Never point the test host at your personal database.

OpenAPI is served at `/openapi/v1.json`. Health at `/api/healthz` checks the database. See [architecture](docs/architecture.md), [ingestion and privacy](docs/ingestion-and-privacy.md), [provider checks](docs/provider-verification.md) and the [acceptance report](docs/acceptance-report.md) for actual validation and remaining limits.

## Backup and restore

Use **Settings → Export your data** for portable JSON notes, highlights, topics, sources, states, trails, practice and source metadata/text. It omits environment secrets and raw original files. This JSON is an interchange export; there is no automatic JSON merge-import. A full recovery uses both database and uploads backups:

```powershell
./scripts/backup.ps1
```

This briefly stops the API while copying a PostgreSQL custom-format dump and the uploads directory into an ignored timestamped `backups/` folder, then restarts it. Keep your private `.env` separately. Backups contain private book text and personal notes; store them accordingly.

Take a backup before upgrading. The catalog upgrade preserves original IDs and resolves merged duplicate links to a surviving item. Restoring the pre-upgrade database and uploads together is the supported way to return to older code; migration downgrades are intentionally blocked because the old model cannot represent the new provenance and aliases.

To restore **into a fresh separate project** without overwriting the current app:

```powershell
$env:PORT = '3002'
docker compose -p rabbithole-restored up -d db
docker compose -p rabbithole-restored cp ./backups/YOUR-TIMESTAMP/database.dump db:/tmp/restore.dump
docker compose -p rabbithole-restored exec -T db pg_restore -U rabbithole -d rabbithole --no-owner /tmp/restore.dump
docker compose -p rabbithole-restored create api
docker compose -p rabbithole-restored cp ./backups/YOUR-TIMESTAMP/uploads/. api:/data/uploads
docker compose -p rabbithole-restored up -d
```

Open `http://localhost:3002`. Use a new project name for each restore. Do not use `--clean` against an existing personal database. Restore the database and files from the same backup, using the same database credential. Migration history in the dump is preserved and newer forward migrations apply on startup.

To verify a backup containing imported items using a temporary isolated installation:

```powershell
./scripts/verify-restore.ps1 -BackupFolder ./backups/YOUR-TIMESTAMP
```

This uses the already built `rabbithole-api` and `rabbithole-web` images on port 3002, checks PostgreSQL search, state/trail persistence and a real PDF across restart, then removes only its uniquely named verification containers and volumes. The main installation and backup are preserved.

## Current limits

- Daily editions roll over at midnight UTC; publication dates are displayed in UTC so date-only research records retain their source calendar day.
- EPUB sections follow spine documents. Several TOC fragments within one XHTML document remain one section. Scanned PDFs need the original-file fallback; there is no OCR or DRM removal.
- OpenAlex/arXiv supply available metadata and abstracts. An access link does not mean a paper's full text was retrieved. Provider citation references link out; they do not automatically import other papers.
  OpenAlex watches seed one newest page, then process recent publication windows with cursor continuation and a seven-day overlap. Continuation may require `OPENALEX_API_KEY`; failures retain the cursor and appear in source health. Interactive search remains relevance ranked. Papers indexed more than seven days late may require manual search.
- YouTube channel feeds and known video URLs, and SoundCloud track/playlist URLs, are supported. Authenticated global search, captions, media downloads, Semantic Scholar and OPML import are not implemented.
- AI has fixture validation and failure coverage, but no live model was tested because no credentials or local model were supplied. Playback availability also remains subject to each media provider; live oEmbed metadata was checked, not uninterrupted playback.
- The ranking/filter pipeline targets a personal catalog and loads candidates into memory. It has no vector index or automatic correctness grading. Changing source/topic preferences takes effect in a new selection; existing editions remain stable.
- JSON export has no automatic merge-import. Use the tested database/files restore for recovery.

## Troubleshooting and deliberate reset

```powershell
docker compose ps
docker compose logs --tail 100 api
docker compose logs -f
docker compose restart api web
docker compose down       # Stops containers; keeps data
```

If port 3000 is occupied, set `PORT=3002` in `.env`. Provider errors are in Settings; retry after backoff or correct the source. Uploaded files without extracted text remain downloadable; OCR and DRM removal are not provided. For an interrupted or failed upload, use **Library → Retry extraction**, or upload the same file again. The existing document/section IDs, notes, edited metadata and reading progress are retained. Reuploading the same file also restores a missing original.

**Destructive reset:** only after taking a backup and deciding to erase this installation, `docker compose down --volumes` deletes the database and uploads. Never use it for routine rebuilds or troubleshooting.

This is a single-user loopback application with no login. Exposing it to LAN/internet requires independent authentication and careful proxy configuration. Do not change the host binding to `0.0.0.0`. The API also validates Host and mutation origins; an explicitly configured `RABBITHOLE_ALLOWED_ORIGIN` is for a trusted local development proxy.
