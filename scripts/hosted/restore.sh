#!/usr/bin/env bash
# Restore ONLY into a fresh isolated project; never overwrites populated database/files.
source "$(dirname "$0")/common.sh"
operation_lock
backup=$(realpath "${1:?Supply an unpacked content backup directory}")
recovery=${2:-}
verify_only=${3:-}
[[ $# -le 3 && ($verify_only == '' || $verify_only == --verify-only) ]] || { echo 'Usage: restore.sh CONTENT [RECOVERY] [--verify-only]' >&2; exit 1; }
[[ -f "$backup/database.dump" && -d "$backup/uploads" && -f "$backup/manifest.json" ]] || { echo 'Backup requires database.dump, uploads, and manifest.json.' >&2; exit 1; }
[[ $(setting INGESTION_PAUSED) == true && $(setting PUBLIC_INDEXING_ENABLED) == false ]] || { echo 'Restore requires paused ingestion and disabled indexing.' >&2; exit 1; }
for volume in database uploads auth_keys; do
  if docker volume inspect "${PROJECT_NAME}_${volume}" >/dev/null 2>&1; then
    echo "Refusing restore: $PROJECT_NAME already has a $volume volume. Use a new isolated project." >&2; exit 1
  fi
done
maintenance
compose up -d --wait --pull never db
compose exec -T db pg_restore -U rabbithole -d rabbithole --no-owner --no-privileges --exit-on-error <"$backup/database.dump"
compose create --no-build api
compose cp "$backup/uploads/." api:/data/uploads
if [[ -n "$recovery" ]]; then
  recovery=$(realpath "$recovery")
  [[ -d "$recovery/auth-keys" && -d "$recovery/caddy-data" ]] || { echo 'Recovery requires auth-keys and caddy-data directories.' >&2; exit 1; }
  compose cp "$recovery/auth-keys/." api:/data/auth-keys
  if [[ "$verify_only" != --verify-only ]]; then
    compose create --no-build gateway
    compose cp "$recovery/caddy-data/." gateway:/data
  fi
fi
# Linux application user must own copied files, including Windows-origin archives.
compose run --rm --no-deps --user root --entrypoint chown api -R app:app /data/uploads /data/auth-keys
if [[ "$verify_only" == --verify-only ]]; then
  compose up -d --wait --no-build --pull never api
else
  compose up -d --wait --no-build --pull never
fi
verify=$(mktemp -d /var/tmp/rabbithole-restore.XXXXXXXX)
trap '[[ "$verify" == /var/tmp/rabbithole-restore.* ]] && rm -rf -- "$verify"' EXIT
compose cp api:/data/uploads "$verify/uploads"
python3 "$RH_RELEASE/scripts/preservation-manifest.py" --baseline "$backup/manifest.json" --output "$STATE_PATH/preservation-verified.json" --uploads "$verify/uploads" -- "${COMPOSE[@]}" exec -T db psql -U rabbithole -d rabbithole -Atqc
echo 'Restored and verified original records and file hashes. Ingestion and indexing remain disabled.'
echo 'Provision/recover admin interactively, then run the authenticated privacy smoke check.'
