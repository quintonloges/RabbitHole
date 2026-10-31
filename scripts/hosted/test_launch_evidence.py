"""Launch must stay closed until the actual current encrypted backup was restored."""
import datetime
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import subprocess
import sys
import unittest

spec = importlib.util.spec_from_file_location('launch_evidence', Path(__file__).with_name('verify-launch-evidence.py'))
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)


class LaunchEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.release = self.root / 'release.env'
        self.release.write_text('API_IMAGE=sha256:' + 'a' * 64 + '\n')
        self.record = {'project': 'rabbithole-hosted', 'releaseSha256': hashlib.sha256(self.release.read_bytes()).hexdigest(),
                       'contentSnapshot': 'c' * 64, 'recoverySnapshot': 'd' * 64, 'pairTag': 'fixture-pair',
                       'capturedAt': datetime.datetime.now(datetime.timezone.utc).isoformat()}
        self.write('last-backup.json', self.record)

    def write(self, name, value):
        (self.root / name).write_text(json.dumps(value))

    def verify(self):
        evidence.verify(self.root, self.release, 'rabbithole-hosted')

    def test_initial_import_preservation_marker_is_insufficient(self):
        self.write('preservation-verified.json', {'passed': True})
        with self.assertRaises(FileNotFoundError):
            self.verify()

    def test_exact_snapshot_pair_and_release_are_accepted(self):
        self.write('backup-restore-verified.json', dict(self.record, passed=True))
        self.verify()

    def test_wrong_snapshot_release_or_project_are_rejected(self):
        for key in ('contentSnapshot', 'recoverySnapshot', 'releaseSha256', 'project', 'pairTag'):
            with self.subTest(key=key):
                self.write('backup-restore-verified.json', dict(self.record, passed=True, **{key: 'wrong'}))
                with self.assertRaises(ValueError):
                    self.verify()

    def test_changed_release_requires_new_backup_and_restore(self):
        self.write('backup-restore-verified.json', dict(self.record, passed=True))
        self.release.write_text('API_IMAGE=sha256:' + 'b' * 64 + '\n')
        with self.assertRaises(ValueError):
            self.verify()

    def test_backup_from_previous_day_cannot_open_site(self):
        record = dict(self.record, capturedAt=(datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(days=2)).isoformat())
        self.write('last-backup.json', record)
        self.write('backup-restore-verified.json', dict(record, passed=True))
        with self.assertRaises(ValueError):
            self.verify()

    def test_deployment_records_pre_migration_pair_and_retry_does_not_replace_it(self):
        source = Path(__file__).with_name('deploy.sh').read_text()
        block = source.split('python3 - "$STATE_PATH" "$previous" "$release" <<\'PY\'\n', 1)[1].split('\nPY\n', 1)[0]
        manifest = self.root / 'last-backup-manifest.json'; manifest.write_text('{}')
        before = dict(self.record, manifestSha256=hashlib.sha256(manifest.read_bytes()).hexdigest())
        self.write('last-backup.json', before)
        def run(previous, release):
            subprocess.run([sys.executable, '-', str(self.root), previous, release], input=block, text=True, check=True, capture_output=True)
        run('previous-release', 'new-release')
        original = json.loads((self.root / 'rollback-backup.json').read_text())
        self.assertEqual(before['contentSnapshot'], original['contentSnapshot'])
        self.assertEqual('previous-release', original['previousRelease'])
        self.write('last-backup.json', dict(before, contentSnapshot='f' * 64))
        run('new-release', 'new-release')
        self.assertEqual(original, json.loads((self.root / 'rollback-backup.json').read_text()))
        run('new-release', 'next-release')
        self.assertEqual('f' * 64, json.loads((self.root / 'rollback-backup.json').read_text())['contentSnapshot'])


if __name__ == '__main__':
    unittest.main()
