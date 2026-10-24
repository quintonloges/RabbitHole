#!/usr/bin/env bash
# Download the real encrypted snapshots and restore in disposable, non-public volumes.
source "$(dirname "$0")/common.sh"
operation_lock
source "$(dirname "$0")/restic-config.sh"
[[ -f "$STATE_PATH/last-backup.json" ]] || { echo 'Run an encrypted off-server backup first.' >&2; exit 1; }
work=$(mktemp -d /var/tmp/rabbithole-restore-test.XXXXXXXX)
test_project="rabbithole-rehearsal-$(python3 -c 'import uuid;print(uuid.uuid4().hex[:12])')"
test_compose=(docker compose --project-name "$test_project" --project-directory "$RH_RELEASE" --env-file "$work/hosted.env" --env-file "$RH_RELEASE/release.env" -f "$RH_RELEASE/compose.hosted.yaml" -f "$RH_RELEASE/compose.staging.yaml")
cleanup() {
  local status=$?
  trap - EXIT
  if [[ -f "$work/hosted.env" && "$test_project" == rabbithole-rehearsal-* ]]; then
    "${test_compose[@]}" down --volumes --remove-orphans || status=1
  fi
  if [[ "$work" == /var/tmp/rabbithole-restore-test.* ]]; then rm -rf -- "$work" || status=1; fi
  exit "$status"
}
trap cleanup EXIT
readarray -t snapshots < <(python3 - "$STATE_PATH/last-backup.json" "$RH_RELEASE/release.env" "$PROJECT_NAME" <<'PY'
import hashlib,json,pathlib,sys
record=json.loads(pathlib.Path(sys.argv[1]).read_text())
if record.get('project')!=sys.argv[3] or record.get('releaseSha256')!=hashlib.sha256(pathlib.Path(sys.argv[2]).read_bytes()).hexdigest():
    raise SystemExit('Back up the current pinned release and project before its restore test.')
for key in ('contentSnapshot','recoverySnapshot'):
    value=record[key]
    if len(value)!=64 or any(c not in '0123456789abcdef' for c in value): raise SystemExit('Invalid recorded snapshot ID.')
    print(value)
PY
)
[[ ${#snapshots[@]} == 2 ]] || { echo 'Missing valid backup snapshot evidence.' >&2; exit 1; }
restic restore "${snapshots[0]}" --target "$work/content-download" >/dev/null
restic -r "$RECOVERY_REPOSITORY" --password-file "$RECOVERY_PASSWORD_FILE" restore "${snapshots[1]}" --target "$work/recovery-download" >/dev/null
# Restic preserves absolute source paths beneath --target; locate exactly one payload.
python3 - "$work" <<'PY'
import hashlib,json,pathlib,sys
root=pathlib.Path(sys.argv[1])
for category,marker in [('content','manifest.json'),('recovery','recovery-manifest.json')]:
    matches=list((root/(category+'-download')).rglob(marker))
    if len(matches)!=1: raise SystemExit('Expected one '+category+' payload in backup.')
    (root/(category+'-path')).write_text(str(matches[0].parent))
recovery=pathlib.Path((root/'recovery-path').read_text())
expected=json.loads((recovery/'recovery-manifest.json').read_text()); actual={}
for file in sorted(recovery.rglob('*')):
    if file.is_symlink(): raise SystemExit('Recovery symlinks are not allowed.')
    if file.is_file() and file.name!='recovery-manifest.json':
        with file.open('rb') as stream: actual[file.relative_to(recovery).as_posix()]=hashlib.file_digest(stream,'sha256').hexdigest()
if actual!=expected: raise SystemExit('Recovery configuration/key/certificate hashes differ.')
PY
content=$(cat "$work/content-path")
recovery=$(cat "$work/recovery-path")
# Pick a subnet disjoint from existing Docker networks. Nothing publishes ports.
docker network ls --quiet >"$work/network-ids"
if [[ -s "$work/network-ids" ]]; then
  xargs docker network inspect <"$work/network-ids" >"$work/networks.json"
else
  printf '[]' >"$work/networks.json"
fi
python3 - "$RH_CONFIG" "$work/hosted.env" "$work/networks.json" "$test_project" "$work/state" <<'PY'
import ipaddress,json,pathlib,sys
source,target,networks,project,state=sys.argv[1:]
used=[ipaddress.ip_network(c['Subnet'],strict=False) for n in json.loads(pathlib.Path(networks).read_text()) for c in n.get('IPAM',{}).get('Config',[]) if c.get('Subnet')]
subnet=next((ipaddress.ip_network(f'172.29.{n}.0/24') for n in range(1,255) if not any(ipaddress.ip_network(f'172.29.{n}.0/24').overlaps(u) for u in used)),None)
if subnet is None: raise SystemExit('No free restore-test subnet; configure one explicitly.')
overrides={'PROJECT_NAME':project,'STATE_PATH':state,'APP_SUBNET':str(subnet),'GATEWAY_IP':str(subnet.network_address+2),'WEB_IP':str(subnet.network_address+3),'API_IP':str(subnet.network_address+4),'STAGING_ORIGIN':'https://quintonloges.me:47834','INGESTION_PAUSED':'true','PUBLIC_INDEXING_ENABLED':'false'}
lines=[x for x in pathlib.Path(source).read_text().splitlines() if x.split('=',1)[0] not in overrides]
pathlib.Path(target).write_text('\n'.join(lines+[k+'='+v for k,v in overrides.items()])+'\n')
PY
RH_CONFIG="$work/hosted.env" bash "$RH_RELEASE/scripts/hosted/restore.sh" "$content" "$recovery" --verify-only
"${test_compose[@]}" cp api:/data/auth-keys "$work/restored-auth-keys"
python3 - "$recovery/auth-keys" "$work/restored-auth-keys" <<'PY'
import hashlib,pathlib,sys
def hashes(root):
    root=pathlib.Path(root); result={}
    for file in root.rglob('*'):
        if file.is_file():
            with file.open('rb') as stream: result[file.relative_to(root).as_posix()]=hashlib.file_digest(stream,'sha256').hexdigest()
    return result
if hashes(sys.argv[1])!=hashes(sys.argv[2]): raise SystemExit('Restored authentication keys differ.')
PY
"${test_compose[@]}" down --volumes --remove-orphans
python3 - "$STATE_PATH/last-backup.json" "$STATE_PATH/backup-restore-verified.json" <<'PY'
import datetime,json,pathlib,sys
record=json.loads(pathlib.Path(sys.argv[1]).read_text()); record.update(passed=True,verifiedAt=datetime.datetime.now(datetime.timezone.utc).isoformat())
target=pathlib.Path(sys.argv[2]); temporary=target.with_suffix('.tmp'); temporary.write_text(json.dumps(record,indent=2)); temporary.replace(target)
PY
echo 'PASS: actual off-server content and recovery snapshots restored; database, uploads, keys, recovery settings, and certificate file hashes verified.'
