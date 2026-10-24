# Public catalog, private workspace, and DigitalOcean operations

The public site is `https://rabbithole.quintonloges.me`; the GitHub repository is `https://github.com/quintonloges/RabbitHole`. The hostname identifies Caddy's certificate, validated host/origin and canonical public links. Configure the Droplet's SSH details and credentials in GitHub Actions secrets; they do not need to be shared in chat. Live deployment also needs the matching CPU architecture and DNS pointing to that server. Encrypted backups stay on the Droplet by default; no off-server destination or storage account is required.

## Application boundaries

| Public | Private administrator |
|---|---|
| `/`, `/search`, `/items/{id}` | `/admin`, `/admin/login`, and all personal pages under `/admin` |
| `/api/public/items` with search, pagination and filters; detail, related, filters, sitemap | Every existing API, OpenAPI, files/ranges, settings, imports, AI, exports, notes, trails and practice |
| Allowlisted metadata, at most 600 characters of a source excerpt, validated links and opt-in embeds | Full permitted source text, uploaded books, source configuration and personal state |

`IsPublic` is persistent and independent of saved/read/dismissed/source-enabled state. Eligible external records are backfilled once and new external imports default public. Refresh preserves a manual hide; merge preserves restrictive visibility. Uploads, chapters, section references, document-derived aliases and private/credential-bearing URLs cannot be published. Public search uses only fields that the public can see; hidden matches never affect counts, filters, related items or sitemap. Public requests do not invoke AI/ingestion or write personal state.

Public pages are server-rendered without administrator credentials and use `no-store`. Admin pages check the API session before rendering private components. Search/filter results, admin and login are `noindex`. `PUBLIC_INDEXING_ENABLED=true` is an explicit launch step; until then robots excludes the site and the sitemap is empty. Hide/unhide affects subsequent requests immediately; an already displayed page remains in the visitor's browser until refreshed.

## Administrator and API clients

Desktop development:

```powershell
./scripts/dev.ps1 start
docker compose exec api dotnet RabbitHole.Api.dll --admin-password
```

The command prompts twice with hidden input; it provisions or resets the sole administrator. It rejects redirected passwords and password arguments. No default credential exists. Reset and password change revoke every existing session; logout revokes the current session. Use `/admin/login` to sign in. Hosted cookies are Secure, HttpOnly, host-only and SameSite=Lax with a 12-hour fixed default expiry. Explicit Development/Testing environments permit loopback HTTP cookies for local work. Persistent Data Protection keys prevent container replacement from invalidating all sessions.

For an API client: GET `/api/auth/csrf` with a cookie jar; POST `/api/auth/login` with `{ "password": "..." }` and `X-CSRF-TOKEN`; obtain another CSRF token after login and use it for every write. GET `/api/auth/session` verifies the session. POST `/api/auth/logout` ends it; POST `/api/auth/password` accepts `currentPassword` and `newPassword`. Edition creation is POST `/api/discover/today`; additional picks use POST `/api/discover/more`. GET today is read-only and can return an empty edition.

Pack/audit scripts now prompt for the password and keep cookies only in process memory. They default to `http://127.0.0.1:47832/api`; hosted targets must be explicitly HTTPS:

```powershell
python scripts/apply-source-pack.py --base https://rabbithole.quintonloges.me/api
python scripts/import-reading-pack.py --base https://rabbithole.quintonloges.me/api --apply
python scripts/audit-recommendations.py --base https://rabbithole.quintonloges.me/api --output data/hosted-audit
```

These clients target the new authenticated app, not the unchanged legacy desktop production image. The legacy rollback verification intentionally uses that image's original API.

## Server setup

Use a Linux Droplet with Docker Engine, Docker Compose **2.24.4 or newer**, Bash, Python **3.11+**, curl, gzip, tar, util-linux (`flock`), restic, and systemd. Permit inbound SSH and Caddy's TCP 80/443 through the DigitalOcean firewall; PostgreSQL, API and Next never publish host ports. An A record for `rabbithole.quintonloges.me` must point to the Droplet; only add AAAA when that server's IPv6 routing works. Do not enable a CDN cache for dynamic catalog/admin responses.

For this deployment, add an A record named `rabbithole` in the `quintonloges.me` zone pointing to `68.183.97.155`; keep the apex domain's existing records. DigitalOcean Cloud Firewalls are [free](https://docs.digitalocean.com/products/networking/firewalls/details/pricing/). Allow inbound TCP 22, 80 and 443 from all IPv4 addresses, with IPv6 equivalents when enabled, and retain the default outbound rules. SSH uses keys with password and keyboard-interactive authentication disabled. Standard GitHub-hosted runners use changing address ranges, so restricting SSH to the owner's home IP would block deployments; see [GitHub runner networking](https://docs.github.com/en/actions/reference/runners/github-hosted-runners#ip-addresses). Leave database, application and staging ports closed to public traffic.

The deployment account needs permission to run Docker, write `/srv/rabbithole` and read/write its private configuration in `/etc/rabbithole`. For a non-root SSH account, assign that account ownership of these dedicated paths and set the backup service User to the same account so operation locks remain writable; Docker access is privileged server access. Configure trusted host keys out of band. Do not use `StrictHostKeyChecking=no` or an unverified `ssh-keyscan` result in CI.

```bash
sudo install -d -m 700 /etc/rabbithole
sudo install -d -m 755 /srv/rabbithole/releases /srv/rabbithole/state
# Install .env.hosted.example as /etc/rabbithole/hosted.env, then edit privately.
sudo chmod 600 /etc/rabbithole/hosted.env
```

Set the real database password, certificate contact email and routing values in `hosted.env`. Keep `INGESTION_PAUSED=true` and `PUBLIC_INDEXING_ENABLED=false` until launch. Copy provider secrets separately from content backups; desktop `host.docker.internal` AI endpoints need explicit replacement. AI remains optional.

Hosted uses project `rabbithole-hosted`, PostgreSQL **17**, and separate `database`, `uploads`, `auth_keys`, `caddy_data`, `caddy_config` volumes. Default trusted proxy addresses are Caddy `172.30.5.2` and Next `172.30.5.3`; the API has explicit address `172.30.5.4` to prevent allocation collisions. Update the subnet and all three addresses together if those networks conflict. Caddy overwrites forwarding headers; the API trusts only the configured exact internal proxies. Public SSR forwards only the validated visitor IP, never cookies. Default rate budgets are 120 public requests/minute/IP, 30 searches/minute/IP and five login attempts/15 minutes/IP, plus persistent account lockout. Health is minimal; private responses use no-store. JSON container logs are capped at three 10-MB files and do not log request bodies or credential values.

## GitHub Actions release

`Validate` runs script tests, backend tests, PostgreSQL migration/HTTP regressions, lint, TypeScript, optimized frontend build, Playwright and hosted config checks. `Deploy to DigitalOcean` is manually dispatched after choosing `staging` or `production` and `linux/amd64` or `linux/arm64`; it runs validation before building. Add the following in [repository Actions secrets](https://github.com/quintonloges/RabbitHole/settings/secrets/actions), or as secrets in the chosen GitHub environment. Environment secrets can target separate staging and production servers. Configure environment release-branch restrictions as appropriate.

| Type | Name | Value |
|---|---|---|
| Secret | `SSH_HOST` | Droplet hostname or IPv4 address |
| Secret | `SSH_USER` | Deployment account |
| Secret, optional | `SSH_PORT` | SSH port; defaults to 22 |
| Secret | `SSH_PRIVATE_KEY` | Dedicated deployment private key |
| Secret | `SSH_KNOWN_HOSTS` | Independently verified server host-key entry; include `[host]:port` for nonstandard ports |

The workflow uses these secrets directly. `SSH_HOST`, `SSH_USER` and `SSH_PORT` also accept Actions variables as a fallback, with secrets taking precedence. `SSH_SECRET` is accepted as a compatibility name for `SSH_PRIVATE_KEY`; when both exist, `SSH_PRIVATE_KEY` takes precedence. A configuration check reports missing names or invalid connection formats before building images, without printing secret values. See [GitHub's secrets instructions](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets) for adding them. Server runtime settings remain in private `/etc/rabbithole/hosted.env` as described above.

The job builds commit-tagged API/web images, includes PostgreSQL 17 and Caddy, transfers a checksummed image archive over verified SSH and installs `/srv/rabbithole/releases/<git-sha>`. `release.env` pins all four runtime images by SHA-256 ID. Reusing a commit with different image IDs is rejected. Retain release directories/image archives and matching backups for rollback.

On first install, deploy loads images and sets the current release pointer **without creating application data volumes**. Restore the personal library before starting the site. On later releases, deploy first creates an encrypted backup of the previous pinned release even when its stack is stopped, then upgrades in maintenance mode. Backups use local storage unless `BACKUP_MODE=offserver` is explicitly configured. Deployment does not automatically open the public site.

## Isolated staging and local rehearsal

Staging uses `/etc/rabbithole/staging.env`, project `rabbithole-staging`, current pointer `/srv/rabbithole/current-staging`, a dedicated `STATE_PATH` (for example `/srv/rabbithole/staging-state`), subnet `172.30.6.0/24`, gateway `172.30.6.2`, web `172.30.6.3` and API `172.30.6.4`. Set `STAGING_ORIGIN` to the explicit HTTPS browser/tunnel origin. Staging Compose binds only `127.0.0.1:47833` (HTTP) and `127.0.0.1:47834` (HTTPS), uses Caddy's private CA, and forces ingestion paused and indexing off. It cannot pass the public launch command.

Reach staging with SSH forwarding, for example `ssh -L 127.0.0.1:8443:127.0.0.1:47834 USER@SERVER`; set `STAGING_ORIGIN=https://rabbithole.quintonloges.me:8443`. Resolve that hostname to loopback on the inspecting machine for this private tunnel. Export Caddy's **public** staging CA certificate (`/data/caddy/pki/authorities/local/root.crt`) through SSH and use it with `curl --cacert`; do not bypass verification for production. Never expose the staging ports or its private CA key.

Desktop rehearsal preserves the current personal library and uses independent disposable volumes:

```powershell
./scripts/dev.ps1 start
./scripts/backup.ps1 -Environment production
./scripts/rehearse-migration.ps1 -BackupFolder ./backups/TIMESTAMP-production
./scripts/check-recommendations-postgres.ps1
# Rollback rehearsal uses the original preserved production images:
./scripts/verify-restore.ps1 -BackupFolder ./backups/TIMESTAMP-production
```

The migration rehearsal compares original-column row hashes across all original tables, stable IDs and every original file hash before upgrade and after restart. It checks all paginated public IDs against document IDs, probes protected APIs and samples private detail requests. Additional automated tests cover alias closure, sessions, ranges, CSRF and mutation behavior. Fixture data stays outside the real library.

## Final cutover and initial restore

After staging passes, freeze local writes and take a final consistent backup:

```powershell
./scripts/backup.ps1 -Environment production -KeepStopped
```

Transfer that private backup directory over SSH to the server. Transfer provider secrets separately. Preserve `.local/production/`, original Docker volumes and image IDs on the desktop; do not remove them. In a shell on the server:

```bash
cd /srv/rabbithole/current
export RH_RELEASE=/srv/rabbithole/current
export RH_CONFIG=/etc/rabbithole/hosted.env
bash scripts/hosted/restore.sh /srv/rabbithole/import/FINAL-BACKUP
source scripts/hosted/common.sh
compose exec api dotnet RabbitHole.Api.dll --admin-password
```

Restore refuses an existing destination database/uploads/auth volume, creates fresh volumes, migrates the restored database and compares the manifest. Maintenance remains enabled, so the public catalog stays unavailable while `/admin` and authenticated checks are usable. For later disaster recovery, the second restore argument is the separately decrypted recovery payload containing authentication keys and Caddy data: `bash scripts/hosted/restore.sh CONTENT RECOVERY`.

## Encrypted nightly backups and verified restoration

`BACKUP_MODE=local` is the default, including when the setting is absent. The first backup creates independent encrypted content/recovery repositories under `/srv/rabbithole/backups/<PROJECT_NAME>/` and generates separate private encryption password files under `/etc/rabbithole/backup-keys/<PROJECT_NAME>/`. Subsequent runs reuse them. These paths are outside Caddy's mounted state directory. No `backup.env`, bucket, cloud credentials or off-server setup is required. The deployment account must be able to create these dedicated directories. Preserve the repositories and their password files together when moving the server; these same-server backups do not survive loss of the Droplet.

Enable the timer and verify a real restore:

```bash
sudo install -m 644 scripts/hosted/rabbithole-backup.service /etc/systemd/system/
sudo install -m 644 scripts/hosted/rabbithole-backup.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now rabbithole-backup.timer
bash scripts/hosted/backup.sh
bash scripts/hosted/restore-test.sh
```

Backups stop writers for a consistent database/files copy, then restore the previous service state. Content snapshots include database, uploads and hash manifest; the separate recovery repository contains hosted configuration, auth keys and certificate data. Restic encrypts both. Retention keeps seven daily and four weekly snapshots; each backup verifies repository integrity. The timer runs around 03:30 UTC with up to 15 minutes of randomized delay; inspect failures with `systemctl status rabbithole-backup.service` and `journalctl -u rabbithole-backup.service`.

`restore-test.sh` reads the **actual recorded snapshot pair** from the selected repositories, restores DB/API in disposable non-public volumes, compares record/upload/key/recovery hashes, and records evidence bound to that release and snapshot pair. It does not merely check that backup files exist. It cleans up only its generated rehearsal project. Initial import preservation is insufficient for this gate.

Off-server storage is optional. To enable it, set `BACKUP_MODE=offserver` in `hosted.env`, install `scripts/hosted/backup.env.example` as private mode-600 `/etc/rabbithole/backup.env`, and configure two private remote restic repositories with separate encryption password files. That configuration is trusted shell input. Initialize the two repositories once:

```bash
set -a; source /etc/rabbithole/backup.env; set +a
restic init
restic -r "$RECOVERY_REPOSITORY" --password-file "$RECOVERY_PASSWORD_FILE" init
```

Off-server mode rejects local destinations, shared repository/password paths and insecure password-file permissions. Run `backup.sh` and `restore-test.sh` after switching repositories. No off-server destination is required in local mode.

```bash
bash scripts/hosted/launch.sh
```

Launch requires initial preservation evidence, a backup less than 24 hours old, a successful restoration of that exact snapshot pair/release, an active/enabled backup timer, a valid HTTPS certificate and an interactive authenticated privacy smoke check. A verified local backup satisfies the backup checks. It then enables indexing and ingestion and removes maintenance. Failed opening checks re-enable maintenance and pause ingestion. Do not create evidence markers by hand or bypass these checks.

## Rollback and troubleshooting

Keep maintenance enabled during recovery. If hosted activity has occurred, first capture it with `bash scripts/hosted/backup.sh --keep-stopped`; the frozen desktop copy only contains pre-cutover state. Select the **matching** release archive, database/files backup and recovery payload, restore into a fresh server/project, provision/recover admin if needed, rerun restore/smoke checks and then switch DNS/traffic. Never downgrade schema by swapping images or overwrite the frozen local copy with unverified hosted data. `restore.sh` deliberately refuses to overwrite existing volumes.

For a pre-cutover desktop rollback, `./scripts/production.ps1 start` starts the preserved images and volumes; run it only after stopping hosted writers. For an ordinary hosted restart, source `common.sh` and run `compose up -d --wait --no-build --pull never`. For bounded logs: `compose logs --tail 100 api web gateway`. Use `/api/healthz` to check readiness. A `401` on a private endpoint is expected without login; `400 CSRF validation failed` requires a fresh identity-bound token; `429` respects `Retry-After`; `403 Host not allowed` requires correct explicit host/proxy configuration, not a wildcard. Account recovery uses the interactive command above, with no email dependency.

The current public catalog computes eligibility and public metadata filtering in memory after database projection. This preserves privacy and is suitable for the measured personal library, but large catalogs need profiling before promising high public traffic. Live-server backup/restoration, DNS/ACME issuance and real DigitalOcean cutover are not considered verified until the corresponding commands actually run; current evidence is in [acceptance-report.md](acceptance-report.md).

Implementation references: [ASP.NET cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0), [antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0), [Data Protection persistence](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0), [Caddy automatic HTTPS](https://caddyserver.com/docs/automatic-https), [GitHub deployment environments](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/control-deployments), [restic retention](https://restic.readthedocs.io/en/stable/060_forget.html).
