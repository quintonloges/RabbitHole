#!/usr/bin/env bash
set -euo pipefail
umask 077
RH_CONFIG=${RH_CONFIG:-/etc/rabbithole/hosted.env}
RH_RELEASE=${RH_RELEASE:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}
[[ -f "$RH_CONFIG" && -f "$RH_RELEASE/release.env" ]] || { echo 'Missing hosted.env or pinned release.env.' >&2; exit 1; }
# Read only non-secret routing values; never execute a dotenv file as shell code.
setting() { python3 -c 'import sys; print(next((x.split("=",1)[1].strip().strip("\"\x27") for x in open(sys.argv[1]) if x.startswith(sys.argv[2]+"=")), ""))' "$RH_CONFIG" "$1"; }
PROJECT_NAME=$(setting PROJECT_NAME)
PUBLIC_HOST=$(setting PUBLIC_HOST)
STATE_PATH=$(setting STATE_PATH)
[[ "$PROJECT_NAME" =~ ^rabbithole-(hosted|staging|rehearsal-[a-z0-9-]+)$ ]] || { echo 'Invalid isolated hosted project name.' >&2; exit 1; }
[[ "$PUBLIC_HOST" =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]+$ && "$PUBLIC_HOST" != *..* ]] || { echo 'Set a valid public hostname.' >&2; exit 1; }
[[ "$STATE_PATH" == /* && "$STATE_PATH" != / ]] || { echo 'STATE_PATH must be an absolute dedicated directory.' >&2; exit 1; }
STATE_PATH=$(realpath -m "$STATE_PATH")
[[ "$STATE_PATH" != / ]] || { echo 'STATE_PATH must resolve to a dedicated directory below root.' >&2; exit 1; }
[[ $(stat -c '%a' "$RH_CONFIG") == 600 ]] || { echo 'hosted.env must have permissions 600.' >&2; exit 1; }
for key in API_IMAGE WEB_IMAGE DB_IMAGE CADDY_IMAGE; do
  value=$(sed -n "s/^$key=//p" "$RH_RELEASE/release.env")
  [[ "$value" =~ ^sha256:[a-f0-9]{64}$ ]] || { echo "Release must pin $key to an image ID." >&2; exit 1; }
done
CURRENT_LINK=/srv/rabbithole/current
COMPOSE=(docker compose --project-name "$PROJECT_NAME" --project-directory "$RH_RELEASE" --env-file "$RH_CONFIG" --env-file "$RH_RELEASE/release.env" -f "$RH_RELEASE/compose.hosted.yaml")
if [[ "$PROJECT_NAME" != rabbithole-hosted ]]; then
  [[ -n $(setting APP_SUBNET) && $(setting APP_SUBNET) != 172.30.5.0/24 && "$STATE_PATH" != /srv/rabbithole/state ]] || { echo 'Staging/rehearsal requires a distinct explicit APP_SUBNET and STATE_PATH.' >&2; exit 1; }
  [[ -n $(setting GATEWAY_IP) && -n $(setting WEB_IP) && -n $(setting API_IP) ]] || { echo 'Set explicit staging/rehearsal gateway, web and API IPs.' >&2; exit 1; }
  CURRENT_LINK="/srv/rabbithole/current-${PROJECT_NAME#rabbithole-}"
  COMPOSE+=(-f "$RH_RELEASE/compose.staging.yaml")
fi
compose() { "${COMPOSE[@]}" "$@"; }
mkdir -p "$STATE_PATH"
maintenance() { touch "$STATE_PATH/maintenance"; chmod 644 "$STATE_PATH/maintenance"; }
operation_lock() {
  # A deployment may call backup while holding this same inherited lock description.
  if [[ $(readlink "/proc/$$/fd/9" 2>/dev/null || true) != "$STATE_PATH/operation.lock" ]]; then
    exec 9>"$STATE_PATH/operation.lock"
  fi
  flock -n 9 || { echo 'Another backup/deployment/restore is running.' >&2; exit 1; }
}
set_setting() {
  python3 - "$RH_CONFIG" "$1" "$2" <<'PY'
import pathlib, sys
p=pathlib.Path(sys.argv[1]); k,v=sys.argv[2:]
lines=[line for line in p.read_text().splitlines() if not line.startswith(k+'=')]
p.write_text('\n'.join([*lines,k+'='+v])+'\n')
PY
}
