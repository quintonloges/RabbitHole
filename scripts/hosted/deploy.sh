#!/usr/bin/env bash
# Run after CI transfers and verifies an image bundle into a new release directory.
set -euo pipefail
umask 077
release=$(cd "${1:?Supply the absolute release directory}" && pwd)
[[ "$release" =~ ^/srv/rabbithole/releases/[a-f0-9]{40}$ ]] || { echo 'Use /srv/rabbithole/releases/<40-character Git SHA>.' >&2; exit 1; }
cd "$release"
sha256sum --check bundle.sha256
docker load -i images.tar.gz >/dev/null
release_id=${release##*/}
for pair in API_IMAGE:rabbithole-api WEB_IMAGE:rabbithole-web DB_IMAGE:rabbithole-postgres CADDY_IMAGE:rabbithole-caddy; do
  key=${pair%%:*}; image=${pair#*:}
  printf '%s=%s\n' "$key" "$(docker image inspect "$image:$release_id" --format '{{.Id}}')"
done >release.env.tmp
if [[ -f release.env ]] && ! cmp --silent release.env release.env.tmp; then
  rm -f -- release.env.tmp
  echo 'This Git release already pins different images. Use a new commit/release; existing release pins are immutable.' >&2
  exit 1
fi
mv -- release.env.tmp release.env
export RH_RELEASE="$release"
source scripts/hosted/common.sh
operation_lock
[[ ! -e "$CURRENT_LINK" || -L "$CURRENT_LINK" ]] || { echo 'Current release pointer must be a symlink, not a directory.' >&2; exit 1; }
maintenance
if docker volume inspect "${PROJECT_NAME}_database" >/dev/null 2>&1; then
  [[ -L "$CURRENT_LINK" ]] || { echo 'Existing database has no prior pinned release. Refusing upgrade without a known backup command.' >&2; exit 1; }
  # A stopped stack still holds the only personal data. Start its old DB if needed,
  # store a consistent encrypted backup, then keep all old services stopped.
  previous=$(readlink -f "$CURRENT_LINK")
  RH_RELEASE="$previous" bash "$previous/scripts/hosted/backup.sh" --keep-stopped
  # Invalidate prior verification before applying migrations to the preserved data.
  rm -f -- "$STATE_PATH/preservation-verified.json"
  python3 - "$STATE_PATH" <<'PY'
import hashlib,json,pathlib,sys
state=pathlib.Path(sys.argv[1]); record=json.loads((state/'last-backup.json').read_text())
if record.get('manifestSha256')!=hashlib.sha256((state/'last-backup-manifest.json').read_bytes()).hexdigest():
    raise SystemExit('The pre-upgrade backup manifest does not match backup evidence.')
PY
else
  set_setting INGESTION_PAUSED true
  set_setting PUBLIC_INDEXING_ENABLED false
  ln -sfn -- "$release" "$CURRENT_LINK"
  echo 'Release images loaded and pinned. No application volumes were created.'
  echo 'Restore the verified personal backup with restore.sh before starting the stack; then provision admin and run backup/restore-test/launch checks.'
  exit 0
fi
set_setting INGESTION_PAUSED true
set_setting PUBLIC_INDEXING_ENABLED false
# Once migration starts, recovery must use this release and its matching snapshot;
# an older executable must never be resumed automatically against a new schema.
ln -sfn -- "$release" "$CURRENT_LINK"
compose up -d --wait --no-build --pull never
verify=$(mktemp -d /var/tmp/rabbithole-upgrade.XXXXXXXX)
trap '[[ "$verify" == /var/tmp/rabbithole-upgrade.* ]] && rm -rf -- "$verify"' EXIT
compose cp api:/data/uploads "$verify/uploads"
python3 "$RH_RELEASE/scripts/preservation-manifest.py" --baseline "$STATE_PATH/last-backup-manifest.json" --output "$STATE_PATH/preservation-verified.json" --uploads "$verify/uploads" -- "${COMPOSE[@]}" exec -T db psql -U rabbithole -d rabbithole -Atqc
echo 'Pinned release started in maintenance mode. Admin is available; ingestion and indexing are paused.'
echo 'Run preservation rehearsal and authenticated smoke checks before launch.sh.'
