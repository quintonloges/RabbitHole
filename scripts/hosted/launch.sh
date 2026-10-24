#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
operation_lock
[[ "$PROJECT_NAME" == rabbithole-hosted ]] || { echo 'Staging and rehearsal projects cannot be launched or indexed.' >&2; exit 1; }
[[ -f "$STATE_PATH/preservation-verified.json" ]] || { echo 'Restore/preservation rehearsal must pass first.' >&2; exit 1; }
python3 "$RH_RELEASE/scripts/hosted/verify-launch-evidence.py" "$STATE_PATH" "$RH_RELEASE/release.env" "$PROJECT_NAME"
systemctl is-enabled --quiet rabbithole-backup.timer || { echo 'Enable the nightly backup timer before launch.' >&2; exit 1; }
systemctl is-active --quiet rabbithole-backup.timer || { echo 'Start the nightly backup timer before launch.' >&2; exit 1; }
prefix=$(mktemp /var/tmp/rabbithole-compose.XXXXXXXX)
opening=false
cleanup() {
  local status=$?
  trap - EXIT
  if [[ "$status" != 0 && "$opening" == true ]]; then
    maintenance
    set_setting PUBLIC_INDEXING_ENABLED false
    set_setting INGESTION_PAUSED true
    compose up -d --wait --no-build --pull never || true
  fi
  rm -f -- "$prefix"
  exit "$status"
}
trap cleanup EXIT
python3 -c 'import json,sys; json.dump(sys.argv[2:],open(sys.argv[1],"w"))' "$prefix" "${COMPOSE[@]}"
python3 "$RH_RELEASE/scripts/hosted/smoke.py" --base "https://$PUBLIC_HOST/api" --output "$STATE_PATH/smoke-verified.json" --compose-prefix "$prefix"
# This command fails on a missing/untrusted certificate or an unavailable HTTPS gateway.
curl --fail --silent --show-error "https://$PUBLIC_HOST/api/healthz" >/dev/null
opening=true
set_setting PUBLIC_INDEXING_ENABLED true
set_setting INGESTION_PAUSED false
compose up -d --wait --no-build --pull never
rm -f -- "$STATE_PATH/maintenance"
curl --fail --silent --show-error "https://$PUBLIC_HOST/api/public/items?pageSize=1" >/dev/null
echo "RabbitHole is live at https://$PUBLIC_HOST. Preserve the stopped desktop recovery copy."
