# Desktop environments

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

## What protects production

Production has no build contexts or source-code mounts. Its web/API/database/gateway images are pinned to immutable IDs and retained under local `rabbithole-production-*:snapshot-*` tags. Development uses `rabbithole-dev-api` and `rabbithole-dev-web`, so rebuilding it does not retag production. Production's Caddyfile is a separate snapshot too. A separate source checkout is unnecessary for this deployment because production runs the frozen images rather than the editable source tree.

Both production data volumes are explicitly named and marked `external: true`. A missing original volume causes startup to fail instead of creating an empty replacement. Compose leaves external volumes alone on teardown. Backups remain necessary: Docker Desktop reset, manual volume deletion, or Docker pruning can still destroy local assets.

Keep `.local/production/` privately backed up separately from the database/files backups. It contains effective server configuration and may contain provider secrets. It is excluded from Git and Docker build contexts. Never attach its contents to an issue or print `docker compose config` for production in shared logs. Its `.env` is intentionally empty; environment values live in `compose.json`. Compose interpolates JSON strings, so literal `$` characters in manually edited settings must be escaped as `$$`.

The root `.env` belongs only to development. Do not copy production credentials into it. Explicitly enable any desired development providers and add sources through the app; development begins without subscriptions or imported items. Pack and audit scripts target development by default. To operate on the personal library, specify `--base http://127.0.0.1:47831/api` explicitly.

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

Both gateways still bind only to `127.0.0.1`. No Windows firewall rules, router forwarding, remote-host exceptions, or LAN exposure were added. Phone access requires a subsequent protected proxy or private-network setup. The app still has no built-in login. The unusual ports prevent common development collisions; they do not provide authentication.

Docker documents [project isolation](https://docs.docker.com/compose/how-tos/project-name/), [external volumes](https://docs.docker.com/reference/compose-file/volumes/), and [production Compose deployments](https://docs.docker.com/compose/how-tos/production/).
