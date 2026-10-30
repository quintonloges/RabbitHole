# Desktop environments

The public/private migration adds a **third**, independent hosted environment. `compose.hosted.yaml` uses project `rabbithole-hosted` for DigitalOcean and `compose.staging.yaml` adds an isolated loopback staging setup. The original personal instance below remains a preserved recovery copy; do not replace its deployment snapshot from this checkout. See [public hosting and migration](public-hosting.md) for GitHub Actions, HTTPS, authenticated clients, final cutover, encrypted backups and rollback. Development now requires a provisioned admin account at `/admin/login`; its public catalog is available anonymously. `backup.ps1 -KeepStopped` preserves the old stack stopped for final cutover; ordinary backups restore its previous running state.

The existing Docker Desktop engine runs two independent stacks. No second Docker installation or virtual machine is required.

| | Personal production | Development |
|---|---|---|
| Browser address | http://localhost:47831 | http://localhost:47832 |
| Compose project | `rabbithole` (original) | `rabbithole-dev` |
| Database volume | `rabbithole_database` (original) | `rabbithole-dev_database` |
| Uploads volume | `rabbithole_uploads` (original) | `rabbithole-dev_uploads` |
| Network | `rabbithole_default` | `rabbithole-dev_default` |
| Configuration | `.local/production/compose.json` | `compose.yaml` and `.env` |
| Gateway configuration | `.local/production/Caddyfile` | `Caddyfile` |
| Application version | Preserved images, pinned by SHA-256 ID | Built from the working checkout |
| Initial data | Existing personal library | Empty library, editable starter topics |

The setup on 18 October 2026 preserved the running production images and effective container configuration before changing any ports. Its original database credential and provider settings were retained. Development received its own generated database credential and no provider keys. Nothing was copied from the personal library into development.

## Daily commands

Run from the repository in PowerShell:

```powershell
./scripts/production.ps1 status
./scripts/production.ps1 start
./scripts/dev.ps1 start
```

`dev.ps1 start` builds changes and waits for the full stack to become healthy. Development runs the same containerized application as production; it is not a hot-reload server. Rerun `dev.ps1 start` after changing application code. For hot reload, use the separate SQLite/Next commands in the README on port 47834. Automated browser tests remain on their existing isolated ports 3001/5081; internal container port 3000 is not a desktop port.

Both environment scripts accept `status`, `logs`, `start`, `stop` and `restart`. Starting production cannot build or pull a newer image. All services use `restart: unless-stopped`, so running stacks return when Docker Desktop's engine restarts. Docker Desktop itself must be running and the desktop must be awake; enable Docker Desktop's startup setting yourself if desired. A deliberately stopped environment stays stopped until started again.

The wrappers specify the project, Compose file, project directory and environment file explicitly. An unrelated current working directory or `COMPOSE_PROJECT_NAME` setting cannot redirect them. Plain `docker compose` now defaults to development, unless you explicitly override its project/configuration.

## Impeccable live design sessions

Impeccable needs a source-mounted Next development server for hot reload. The opt-in `compose.live.yaml` changes only the development web service and keeps the existing gateway at `http://127.0.0.1:47832`, development API, and development data. It uses a separate `rabbithole-dev-web-live` image and separate dependency/build-cache volumes. The standard development image remains available. Production and hosted manifests do not use this override.

From the repository root, start the standard stack if needed, then enable hot reload:

```powershell
./scripts/dev.ps1 start
docker compose -p rabbithole-dev --project-directory . --env-file .env -f compose.yaml -f compose.live.yaml up --build -d --no-deps --wait web
```

The installed Impeccable 4.3.1 skill uses engine 0.1.5. Its launcher downloads the pinned Windows binary from its release channel and verifies the SHA-256 sidecar before execution. It is cached outside the repository under `%USERPROFILE%\.impeccable\bin\0.1.5\impeccable.exe`. To verify or provision that version:

```powershell
$impeccableLauncher = Join-Path $env:USERPROFILE '.codex/plugins/cache/openai-curated-remote/impeccable/4.3.1/skills/impeccable/scripts/impeccable.cmd'
& $impeccableLauncher engine-probe
```

Use the launcher belonging to the installed skill when upgrading versions. In Codex, ask for `$impeccable live` against the development app. The verified entry is:

```powershell
& $impeccableLauncher live --target apps/web/app/layout.tsx
```

The helper resolves the app root to `apps/web`; subsequent `live-poll`, `live-status`, and `live-server stop` commands run from that directory. The agent must keep servicing the foreground `live-poll` event loop while the browser session is active. Open the app on port **47832**; port **8400** belongs to the loopback helper, not RabbitHole.

Root `PRODUCT.md`, `DESIGN.md`, and `.impeccable/design.json` describe the product and incumbent interface. App-relative injection settings live in `apps/web/.impeccable/live/config.json`. Next's `allowedDevOrigins` includes only the two explicit loopback hostnames used by desktop development. The development response has no CSP requiring a live-mode exception.

The helper temporarily injects its local script into `apps/web/app/layout.tsx`. Stop it before building or deploying application images:

```powershell
Push-Location apps/web
try { & $impeccableLauncher live-server stop } finally { Pop-Location }
./scripts/dev.ps1 start
```

The stop command removes its injection; `dev.ps1 start` restores the standard compiled development service. For a verification session with no application changes, `docker compose -p rabbithole-dev --project-directory . --env-file .env -f compose.yaml up -d --no-deps --no-build --wait web` restores the already-built standard web image. Leave data and cache volumes intact. Helper tokens, machine paths, journals, and previews are ignored by Git, and the app Docker context excludes local Impeccable artifacts.

If edits reload the entire document or do not appear, inspect development web logs for blocked `/_next/hmr` requests and confirm the explicit loopback origins. A visible toolbar alone does not prove hot reload; verification should cover a source change and restoration in the same browser document. Never solve local helper issues by changing the production gateway, public exposure, authentication, or CSP.

## What protects production

Production has no build contexts or source-code mounts. Its web/API/database/gateway images are pinned to immutable IDs and retained under local `rabbithole-production-*:snapshot-*` tags. Development uses `rabbithole-dev-api` and `rabbithole-dev-web`, so rebuilding it does not retag production. Production's Caddyfile is a separate snapshot too. A separate source checkout is unnecessary for this deployment because production runs the frozen images rather than the editable source tree.

Both production data volumes are explicitly named and marked `external: true`. A missing original volume causes startup to fail instead of creating an empty replacement. Compose leaves external volumes alone on teardown. Backups remain necessary: Docker Desktop reset, manual volume deletion, or Docker pruning can still destroy local assets.

Keep `.local/production/` privately backed up separately from the database/files backups. It contains effective server configuration and may contain provider secrets. It is excluded from Git and Docker build contexts. Never attach its contents to an issue or print `docker compose config` for production in shared logs. Its `.env` is intentionally empty; environment values live in `compose.json`. Compose interpolates JSON strings, so literal `$` characters in manually edited settings must be escaped as `$$`.

The root `.env` belongs only to development. Do not copy production credentials into it. Explicitly enable any desired development providers and add sources through the app; development begins without subscriptions or imported items. Pack and audit scripts now authenticate and target development by default. Use an explicit HTTPS base for the migrated hosted library; the preserved legacy desktop image retains its original API contract.

## Backups and checks

```powershell
./scripts/backup.ps1                         # Production, database plus original files
./scripts/backup.ps1 -Environment dev         # Development only
./scripts/check-environments.ps1             # Temporary dev writes, restart, isolation checks
./scripts/verify-restore.ps1 -BackupFolder ./backups/YOUR-BACKUP-FOLDER
```

The backup script pauses the selected API while copying both parts, then restores its previous running state. Database and provider credentials are excluded. Restore verification uses production's pinned images, temporary project-specific volumes, and port 47833. Scheduled ingestion is disabled in the restored copy. It tests a real fixture PDF and state/trail persistence across restart, then deletes only that temporary installation. See the README for retaining a restored installation instead of a temporary check.

The initial pre-separation backup is `backups/20261018-010644/`. The production snapshot's `original-compose.json` records the old resolved deployment, and `release.json` records the image IDs captured during separation. These private files are recovery evidence, not development configuration.

## Deliberate production releases

This setup does not automatically promote development. When a production deployment is requested:

1. Build and test development with `./scripts/dev.ps1 start` and the repository checks. Review the actual application there.
2. Run `./scripts/backup.ps1`. Keep its database/files pair and a private copy of the current `.local/production/` folder together for recovery.
3. Obtain the approved running image IDs with `docker inspect --format '{{.Image}}' rabbithole-dev-api-1` and the equivalent `rabbithole-dev-web-1` command. Retain these IDs under unique local production release tags with `docker image tag`.
4. Change only `services.api.image` and `services.web.image` in `.local/production/compose.json` to those reviewed image IDs. Preserve production environment values, volumes, ports and network. Do not replace its manifest with the development file. Database/gateway or configuration upgrades are separate deliberate changes.
5. Run `./scripts/production.ps1 start`, then check health, library counts, saved state and logs. This may apply database migrations. If recovery is needed after a migration, restore the matching database/files backup with the prior images; merely switching back an API image can be unsafe.

After a release, the production manifest determines the active version. The initial `release.json` remains a record of the original snapshot. Do not prune retained release images before deciding their recovery window has ended. A future move to different CPU architecture, such as a Raspberry Pi, requires compatible builds and a tested database/files restore; these desktop image IDs are not a portable ARM release.

## Network boundary

Both desktop gateways still bind only to `127.0.0.1`. No Windows firewall rules, router forwarding or LAN exposure were added. The current development app requires built-in administrator authentication for its workspace and existing APIs; the frozen legacy production image retains its previous behavior. Hosted HTTPS access is a separate configuration with validated hosts/origins/proxies. The unusual desktop ports prevent collisions; they do not provide authentication.

Docker documents [project isolation](https://docs.docker.com/compose/how-tos/project-name/), [external volumes](https://docs.docker.com/reference/compose-file/volumes/), and [production Compose deployments](https://docs.docker.com/compose/how-tos/production/).
