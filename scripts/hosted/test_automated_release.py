"""Run release/launch shell control flow in an isolated filesystem with fake external commands.

Production paths are relocated in copied scripts; no real Docker, server, credentials or volumes are used.
Existing restic and launch-evidence tests exercise their real implementation separately.
"""
import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

SCRIPTS = Path(__file__).resolve().parent


@unittest.skipUnless(os.name == 'posix' and shutil.which('bash'), 'Requires POSIX shell and symlinks')
class AutomatedReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.release = self.root / 'releases' / ('b' * 40)
        self.previous = self.root / 'releases' / ('a' * 40)
        self.hosted = self.release / 'scripts/hosted'
        self.hosted.mkdir(parents=True); self.previous.mkdir(parents=True)
        (self.root / 'current').symlink_to(self.previous)
        (self.root / 'state').mkdir(); (self.root / 'temp').mkdir(); (self.root / 'bin').mkdir()
        self.log = self.root / 'calls'
        self.environment = dict(os.environ, FIXTURE_ROOT=str(self.root), FIXTURE_LOG=str(self.log),
                                PATH=str(self.root / 'bin') + os.pathsep + os.environ['PATH'])
        self.write('release.sh', SCRIPTS.joinpath('release.sh').read_text()
                   .replace('/srv/rabbithole', str(self.root)).replace('/var/tmp/', str(self.root / 'temp') + '/'))
        self.write('common.sh', '''set -euo pipefail
PROJECT_NAME=${FIXTURE_PROJECT:-rabbithole-hosted}
PUBLIC_HOST=fixture.invalid
STATE_PATH="$FIXTURE_ROOT/state"
COMPOSE=(fixture-compose)
operation_lock() { echo lock >>"$FIXTURE_LOG"; }
maintenance() { touch "$STATE_PATH/maintenance"; }
set_setting() { echo "$1=$2" >>"$FIXTURE_LOG"; }
compose() { echo compose >>"$FIXTURE_LOG"; }
''')
        for stage in ('deploy', 'backup', 'restore-test', 'launch'):
            self.write(stage + '.sh', f'''set -euo pipefail
echo {stage} >>"$FIXTURE_LOG"
[[ "${{FAIL_STAGE:-}}" != {stage} ]] || exit 9
'''+ ('touch "$FIXTURE_ROOT/state/maintenance"\n' if stage == 'deploy' else '')
                 + ('rm -f "$FIXTURE_ROOT/state/maintenance"\n' if stage == 'launch' else ''))
        self.write('smoke.py', '''import os,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from admin_client import read_password_file
password=read_password_file(sys.argv[sys.argv.index('--password-file')+1])
with open(os.environ['FIXTURE_LOG'],'a') as f: f.write('preflight\\n')
if password!='TEST FIXTURE password' or os.environ.get('FAIL_STAGE')=='preflight': raise SystemExit(9)
''')
        shutil.copyfile(SCRIPTS.parent / 'admin_client.py', self.release / 'scripts/admin_client.py')
        self.release.joinpath('scripts/apply-source-pack.py').write_text('''import os
with open(os.environ['FIXTURE_LOG'],'a') as f: f.write('sources\\n')
if os.environ.get('FAIL_STAGE')=='sources': raise SystemExit(9)
''')
        self.binary('systemctl', 'echo "timer-$1" >>"$FIXTURE_LOG"\n[[ "${FAIL_STAGE:-}" != timer ]]')
        self.binary('restic', 'exit 0')
        self.binary('curl', 'echo "https-probe" >>"$FIXTURE_LOG"\n[[ "${FAIL_STAGE:-}" != https ]]')

    def write(self, name, body):
        self.hosted.joinpath(name).write_text(body)

    def binary(self, name, body):
        path = self.root / 'bin' / name
        path.write_text('#!/usr/bin/env bash\nset -euo pipefail\n' + body + '\n'); path.chmod(0o700)

    def run_release(self, fail=None, password='TEST FIXTURE password', sources=False):
        env = dict(self.environment, FAIL_STAGE=fail or '')
        result = subprocess.run(['bash', str(self.hosted / 'release.sh'), str(self.release), *(['--apply-source-pack'] if sources else [])],
                                input=password, capture_output=True, text=True, env=env)
        self.assertNotIn(password, result.stdout + result.stderr)
        self.assertEqual([], list((self.root / 'temp').glob('rabbithole-release.*')), 'Temporary credential was retained')
        return result, self.log.read_text().splitlines() if self.log.exists() else []

    def test_complete_release_runs_every_gate_before_opening_and_sources_are_explicit(self):
        result, calls = self.run_release(sources=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(['lock', 'preflight', 'timer-is-enabled', 'timer-is-active', 'deploy', 'backup', 'restore-test', 'launch', 'sources'], calls)
        self.assertFalse((self.root / 'state/maintenance').exists())

    def test_wrong_password_leaves_current_site_untouched(self):
        result, calls = self.run_release(password='WRONG FIXTURE password')
        self.assertNotEqual(0, result.returncode); self.assertEqual(['lock', 'preflight'], calls)
        self.assertFalse((self.root / 'state/maintenance').exists())

    def test_each_failed_gate_stops_before_later_steps_and_removes_credentials(self):
        stages = ['preflight', 'timer', 'deploy', 'backup', 'restore-test', 'launch']
        for stage in stages:
            with self.subTest(stage=stage):
                self.log.unlink(missing_ok=True)
                result, calls = self.run_release(fail=stage)
                self.assertNotEqual(0, result.returncode)
                if stage != 'launch': self.assertNotIn('launch', calls)
                self.assertNotIn('sources', calls)

    def test_failed_source_activation_does_not_claim_success_or_reclose_the_live_site(self):
        result, calls = self.run_release(fail='sources', sources=True)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(['launch', 'sources'], calls[-2:])
        self.assertNotIn('Approved release completed', result.stdout)
        self.assertFalse((self.root / 'state/maintenance').exists())

    def prepare_real_launch(self):
        self.write('launch.sh', SCRIPTS.joinpath('launch.sh').read_text().replace('/var/tmp/', str(self.root / 'temp') + '/'))
        shutil.copyfile(SCRIPTS / 'verify-launch-evidence.py', self.hosted / 'verify-launch-evidence.py')
        pins = self.release / 'release.env'; pins.write_text('API_IMAGE=sha256:' + 'c' * 64 + '\n')
        record = {'project': 'rabbithole-hosted', 'releaseSha256': hashlib.sha256(pins.read_bytes()).hexdigest(),
                  'contentSnapshot': 'd' * 64, 'recoverySnapshot': 'e' * 64, 'pairTag': 'TEST FIXTURE',
                  'capturedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'passed': True}
        state = self.root / 'state'
        for name in ('last-backup.json', 'backup-restore-verified.json', 'preservation-verified.json'):
            state.joinpath(name).write_text(json.dumps(record))
        state.joinpath('maintenance').touch()
        password = self.root / 'password'; password.write_text('TEST FIXTURE password'); password.chmod(0o600)
        self.environment['RH_RELEASE'] = str(self.release)
        return password

    def test_real_launch_opens_only_after_evidence_and_privacy_checks(self):
        password = self.prepare_real_launch()
        result = subprocess.run(['bash', str(self.hosted / 'launch.sh'), '--password-file', str(password)],
                                capture_output=True, text=True, env=self.environment)
        self.assertEqual(0, result.returncode, result.stderr)
        calls = self.log.read_text().splitlines()
        self.assertLess(calls.index('preflight'), calls.index('PUBLIC_INDEXING_ENABLED=true'))
        self.assertEqual(3, calls.count('https-probe'))
        self.assertFalse((self.root / 'state/maintenance').exists())

    def test_real_launch_keeps_maintenance_when_privacy_check_fails(self):
        password = self.prepare_real_launch()
        result = subprocess.run(['bash', str(self.hosted / 'launch.sh'), '--password-file', str(password)],
                                capture_output=True, text=True, env=dict(self.environment, FAIL_STAGE='preflight'))
        self.assertNotEqual(0, result.returncode)
        self.assertNotIn('PUBLIC_INDEXING_ENABLED=true', self.log.read_text())
        self.assertTrue((self.root / 'state/maintenance').exists())

    def test_real_launch_recloses_if_public_opening_probe_fails(self):
        password = self.prepare_real_launch()
        self.binary('curl', '''echo https-probe >>"$FIXTURE_LOG"
[[ "$*" == *healthz* ]]
''')
        result = subprocess.run(['bash', str(self.hosted / 'launch.sh'), '--password-file', str(password)],
                                capture_output=True, text=True, env=self.environment)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('INGESTION_PAUSED=true', self.log.read_text())
        self.assertIn('PUBLIC_INDEXING_ENABLED=false', self.log.read_text())
        self.assertTrue((self.root / 'state/maintenance').exists())


if __name__ == '__main__':
    unittest.main()
