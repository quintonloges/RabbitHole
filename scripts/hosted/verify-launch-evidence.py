"""Require a verified restoration of the exact off-server snapshot pair and release."""
import hashlib
import datetime
import json
from pathlib import Path
import sys


def verify(state, release, project):
    state = Path(state)
    backup = json.loads((state / 'last-backup.json').read_text())
    restored = json.loads((state / 'backup-restore-verified.json').read_text())
    digest = hashlib.sha256(Path(release).read_bytes()).hexdigest()
    if backup.get('project') != project or backup.get('releaseSha256') != digest:
        raise ValueError('Complete an off-server backup of this project and pinned release first.')
    captured = datetime.datetime.fromisoformat(backup['capturedAt'])
    if not datetime.timedelta(0) <= datetime.datetime.now(datetime.timezone.utc) - captured <= datetime.timedelta(hours=24):
        raise ValueError('The pre-launch backup must be less than 24 hours old.')
    for key in ('project', 'releaseSha256', 'contentSnapshot', 'recoverySnapshot', 'pairTag'):
        if restored.get(key) != backup.get(key) or not backup.get(key):
            raise ValueError('Restore-test evidence does not match the latest backup and pinned release.')
    if restored.get('passed') is not True:
        raise ValueError('The off-server backup restore test has not passed.')


if __name__ == '__main__':
    try:
        verify(*sys.argv[1:])
    except (OSError, ValueError, KeyError) as error:
        raise SystemExit('Launch withheld: run backup.sh, then restore-test.sh successfully. ' + str(error))
