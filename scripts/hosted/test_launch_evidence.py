"""Launch must stay closed until the actual current encrypted backup was restored."""
import datetime
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
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


if __name__ == '__main__':
    unittest.main()
