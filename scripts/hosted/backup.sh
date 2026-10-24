#!/usr/bin/env bash
# Consistent encrypted off-server content and separately protected recovery backups.
source "$(dirname "$0")/common.sh"
[[ $# == 0 || ($# == 1 && "$1" == --keep-stopped) ]] || { echo 'Usage: backup.sh [--keep-stopped]' >&2; exit 1; }
keep_stopped=false; [[ ${1:-} != --keep-stopped ]] || keep_stopped=true
operation_lock
source "$(dirname "$0")/restic-config.sh"
restic cat config >/dev/null
restic -r "$RECOVERY_REPOSITORY" --password-file "$RECOVERY_PASSWORD_FILE" cat config >/dev/null
work=$(mktemp -d /var/tmp/rabbithole-backup.XXXXXXXX)
running=$(compose ps --status running --services)
had_maintenance=false; [[ ! -f "$STATE_PATH/maintenance" ]] || had_maintenance=true
resumed=false
resume() {
  [[ "$resumed" == false ]] || return 0
  # Never resume twice, including after a failed restart.
  resumed=true
  if [[ "$keep_stopped" == false ]]; then
    for service in api web gateway; do
      if grep -qx "$service" <<<"$running"; then compose up -d --wait --no-build --pull never "$service" || return $?; fi
    done
    if ! grep -qx db <<<"$running"; then compose stop db || return $?; fi
    if [[ "$had_maintenance" == false ]]; then rm -f -- "$STATE_PATH/maintenance"; fi
  else
    compose stop api web gateway db || return $?
  fi
}
cleanup() {
  local status=$?
  trap - EXIT
  resume || status=1
  if [[ "$work" == /var/tmp/rabbithole-backup.* ]]; then rm -rf -- "$work" || status=1; fi
  exit "$status"
}
trap cleanup EXIT
maintenance
compose stop api web gateway
compose up -d --wait --no-build --pull never db
mkdir -p "$work/content" "$work/recovery"
compose exec -T db pg_dump -U rabbithole -d rabbithole -Fc >"$work/content/database.dump"
compose cp api:/data/uploads "$work/content/uploads"
compose cp api:/data/auth-keys "$work/recovery/auth-keys"
compose cp gateway:/data "$work/recovery/caddy-data"
cp -- "$RH_CONFIG" "$work/recovery/hosted.env"
cp -- "$RH_RELEASE/release.env" "$work/content/release.env"
cp -- "$RH_RELEASE/release.env" "$work/recovery/release.env"
python3 "$RH_RELEASE/scripts/preservation-manifest.py" --output "$work/content/manifest.json" --uploads "$work/content/uploads" -- "${COMPOSE[@]}" exec -T db psql -U rabbithole -d rabbithole -Atqc
python3 - "$work/recovery" <<'PY'
import hashlib,json,pathlib,sys
root=pathlib.Path(sys.argv[1]); hashes={}
for file in sorted(root.rglob('*')):
    if file.is_symlink(): raise SystemExit('Recovery symlinks are not permitted.')
    if file.is_file():
        with file.open('rb') as stream: hashes[file.relative_to(root).as_posix()]=hashlib.file_digest(stream,'sha256').hexdigest()
(root/'recovery-manifest.json').write_text(json.dumps(hashes,sort_keys=True))
PY
resume
pair="rabbithole-pair-$(python3 -c 'import uuid;print(uuid.uuid4().hex)')"
restic backup "$work/content" --host "$PROJECT_NAME" --tag rabbithole-content --tag "$pair" --json >"$work/content-result.json"
restic -r "$RECOVERY_REPOSITORY" --password-file "$RECOVERY_PASSWORD_FILE" backup "$work/recovery" --host "$PROJECT_NAME" --tag rabbithole-recovery --tag "$pair" --json >"$work/recovery-result.json"
# Group by host: a unique pairing tag must not create an unbounded retention group.
restic forget --host "$PROJECT_NAME" --tag rabbithole-content --group-by host --keep-daily 7 --keep-weekly 4 --prune
restic -r "$RECOVERY_REPOSITORY" --password-file "$RECOVERY_PASSWORD_FILE" forget --host "$PROJECT_NAME" --tag rabbithole-recovery --group-by host --keep-daily 7 --keep-weekly 4 --prune
restic check
restic -r "$RECOVERY_REPOSITORY" --password-file "$RECOVERY_PASSWORD_FILE" check
python3 - "$work" "$STATE_PATH" "$PROJECT_NAME" "$RH_RELEASE/release.env" "$pair" <<'PY'
import datetime,hashlib,json,pathlib,sys
work,state,project,release,pair=sys.argv[1:]; work=pathlib.Path(work); state=pathlib.Path(state)
def snapshot(name):
    summaries=[json.loads(x) for x in (work/name).read_text().splitlines() if x.strip()]
    return next(x['snapshot_id'] for x in reversed(summaries) if x.get('message_type')=='summary')
record={'project':project,'releaseSha256':hashlib.sha256(pathlib.Path(release).read_bytes()).hexdigest(),
        'contentSnapshot':snapshot('content-result.json'),'recoverySnapshot':snapshot('recovery-result.json'),
        'pairTag':pair,'capturedAt':datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'manifestSha256':hashlib.sha256((work/'content/manifest.json').read_bytes()).hexdigest()}
manifest=state/'last-backup-manifest.json.tmp'; manifest.write_bytes((work/'content/manifest.json').read_bytes()); manifest.replace(state/'last-backup-manifest.json')
temporary=state/'last-backup.json.tmp'; temporary.write_text(json.dumps(record,indent=2)); temporary.replace(state/'last-backup.json')
PY
date -u +%FT%TZ >"$STATE_PATH/last-backup"
echo 'Encrypted content and recovery snapshots stored off-server; seven daily and four weekly retained.'
