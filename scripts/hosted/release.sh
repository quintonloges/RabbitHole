#!/usr/bin/env bash
# Approved routine release: stdin carries the existing admin password over verified SSH.
set -euo pipefail
umask 077
[[ $# == 1 || ($# == 2 && "$2" == --apply-source-pack) ]] || { echo 'Usage: release.sh RELEASE [--apply-source-pack] < private-password' >&2; exit 1; }
release=$(cd "$1" && pwd)
release_id=${release##*/}
[[ "$release" =~ ^/srv/rabbithole/releases/[a-f0-9]{40}$ ]] || { echo 'Invalid release directory.' >&2; exit 1; }
[[ ! -t 0 ]] || { echo 'Automated release requires the existing admin password on stdin.' >&2; exit 1; }
[[ -L /srv/rabbithole/current ]] || { echo 'Complete initial restore and admin provisioning before enabling automatic releases.' >&2; exit 1; }
previous=$(readlink -f /srv/rabbithole/current)
[[ "$previous" =~ ^/srv/rabbithole/releases/[a-f0-9]{40}$ ]] || { echo 'The current release pointer is invalid.' >&2; exit 1; }
# Use the installed pins to validate configuration and hold one lock throughout.
export RH_RELEASE="$previous"
source "$release/scripts/hosted/common.sh"
[[ "$PROJECT_NAME" == rabbithole-hosted ]] || { echo 'Automatic public launch is production-only.' >&2; exit 1; }
operation_lock
credential_dir=$(mktemp -d /var/tmp/rabbithole-release.XXXXXXXX)
cleanup() {
  local status=$?
  trap - EXIT
  [[ "$credential_dir" == /var/tmp/rabbithole-release.* ]] && rm -rf -- "$credential_dir"
  exit "$status"
}
trap cleanup EXIT
head -c 4097 >"$credential_dir/password"
# Bad/missing credentials or an unavailable HTTPS endpoint fail before maintenance.
python3 "$release/scripts/hosted/smoke.py" --base "https://$PUBLIC_HOST/api" --check-login-only --password-file "$credential_dir/password"
systemctl is-enabled --quiet rabbithole-backup.timer
systemctl is-active --quiet rabbithole-backup.timer
command -v restic >/dev/null
export RH_RELEASE="$release"
bash "$release/scripts/hosted/deploy.sh" "$release"
# deploy.sh backed up the previous release and verified migration preservation.
# These next two checks bind restoration evidence to the NEW pinned release.
bash "$release/scripts/hosted/backup.sh"
bash "$release/scripts/hosted/restore-test.sh"
bash "$release/scripts/hosted/launch.sh" --password-file "$credential_dir/password"
if [[ ${2:-} == --apply-source-pack ]]; then
  python3 "$release/scripts/apply-source-pack.py" --base "https://$PUBLIC_HOST/api" --pack "$release/config/curated-source-pack.json" --apply --password-file "$credential_dir/password" --report "$STATE_PATH/source-pack-$release_id.json"
fi
echo 'Approved release completed: backup, migration, restoration, authenticated privacy checks and public HTTPS launch passed.'
