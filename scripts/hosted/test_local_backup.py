"""Exercise real local restic encryption/restore and fail-closed path/key handling.

Run on Linux with Python 3.11+, restic, and Bash. All payloads are temporary fixtures.
"""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('local_backup', Path(__file__).with_name('local-backup.py'))
local_backup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(local_backup)


@unittest.skipUnless(os.name == 'posix' and shutil.which('restic'), 'Requires Linux and real restic')
class LocalBackupTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.backups, self.keys = self.root / 'backups', self.root / 'keys'
        self.project = 'rabbithole-hosted'
        self.repository = self.backups / self.project / 'content'
        self.key = self.keys / self.project / 'content.password'

    def prepare(self, initialize=True, state=None):
        local_backup.prepare(self.project, state or self.root / 'state', initialize, self.backups, self.keys)

    def restic(self, *args, category='content', check=True):
        environment = {k: v for k, v in os.environ.items() if not k.startswith('RESTIC_')}
        return subprocess.run(['restic', '-r', str(self.backups / self.project / category), '--password-file',
                               str(self.keys / self.project / (category + '.password')), *args],
                              capture_output=True, text=True, check=check, env=environment)

    def test_same_day_pre_and_post_upgrade_snapshots_both_survive_retention(self):
        self.prepare()
        payload = self.root / 'upgrade'; payload.mkdir()
        snapshots = []
        for version in ('before migration', 'after migration', 'retry backup', 'nightly backup'):
            (payload / 'database').write_text(version)
            result = self.restic('backup', str(payload), '--host', self.project, '--tag', 'rabbithole-content',
                                 '--tag', 'rollback-fixture' if not snapshots else 'later-fixture', '--json')
            self.assertEqual(0, result.returncode, result.stderr)
            snapshots.append(next(row['snapshot_id'] for row in map(json.loads, result.stdout.splitlines()) if row.get('message_type') == 'summary'))
        self.restic('forget', '--host', self.project, '--tag', 'rabbithole-content', '--group-by', 'host',
                    '--keep-last', '2', '--keep-daily', '7', '--keep-weekly', '4', '--keep-tag', 'rollback-fixture', '--prune')
        retained = {row['id'] for row in json.loads(self.restic('snapshots', '--json').stdout)}
        self.assertTrue({snapshots[0], *snapshots[-2:]}.issubset(retained))

    def test_real_encrypted_pair_restore_retention_and_repeated_setup_preserve_keys(self):
        self.prepare()
        key_bytes = self.key.read_bytes()
        recovery_key = self.keys / self.project / 'recovery.password'
        self.assertNotEqual(key_bytes, recovery_key.read_bytes())
        for path in [self.backups, self.keys, self.repository, self.key.parent]:
            self.assertEqual(stat.S_IMODE(path.stat().st_mode), 0o700)
        self.assertEqual(stat.S_IMODE(self.key.stat().st_mode), 0o600)
        content = self.root / 'fixture'
        content.mkdir()
        (content / 'private.txt').write_text('Private fixture payload, never app content.')
        snapshot_ids = []
        for category in ['content', 'recovery']:
            result = self.restic('backup', str(content), '--host', self.project, '--tag', 'fixture-pair', '--json', category=category)
            snapshot_ids.append(next(x['snapshot_id'] for x in map(json.loads, result.stdout.splitlines()) if x.get('message_type') == 'summary'))
            self.restic('forget', '--host', self.project, '--group-by', 'host', '--keep-daily', '7', '--keep-weekly', '4', '--prune', category=category)
            self.restic('check', category=category)
        self.prepare()
        self.prepare(False)
        self.assertEqual(key_bytes, self.key.read_bytes())
        for category, snapshot in zip(['content', 'recovery'], snapshot_ids):
            target = self.root / ('restore-' + category)
            self.restic('restore', snapshot, '--target', str(target), category=category)
            restored = list(target.rglob('private.txt'))
            self.assertEqual(len(restored), 1)
            self.assertEqual(restored[0].read_bytes(), (content / 'private.txt').read_bytes())
        # The recovery key cannot unlock the content repository.
        result = subprocess.run(['restic', '-r', str(self.repository), '--password-file', str(recovery_key), 'cat', 'config'], capture_output=True)
        self.assertNotEqual(result.returncode, 0)

    def test_restore_setup_never_creates_repositories_or_keys(self):
        with self.assertRaisesRegex(ValueError, 'Run backup.sh'):
            self.prepare(False)
        self.assertFalse(self.backups.exists())
        self.assertFalse(self.keys.exists())

    def test_missing_existing_repository_key_is_never_regenerated(self):
        self.prepare()
        config = (self.repository / 'config').read_bytes()
        self.key.unlink()
        with self.assertRaisesRegex(ValueError, 'encryption key is missing'):
            self.prepare()
        self.assertFalse(self.key.exists())
        self.assertEqual(config, (self.repository / 'config').read_bytes())

    def test_unknown_existing_repository_is_never_initialized(self):
        self.repository.mkdir(parents=True, mode=0o700)
        for directory in (self.backups, self.backups / self.project):
            directory.chmod(0o700)
        marker = self.repository / 'do-not-overwrite'
        marker.write_text('Existing unrelated content')
        with self.assertRaisesRegex(ValueError, 'Existing repository has no config'):
            self.prepare()
        self.assertEqual(marker.read_text(), 'Existing unrelated content')
        self.assertFalse(self.keys.exists())

    def test_gateway_state_overlap_is_rejected_before_any_creation(self):
        for state in [self.root, self.backups, self.keys, self.backups / self.project,
                      self.keys / self.project, self.repository, self.key]:
            with self.subTest(state=state), self.assertRaisesRegex(ValueError, 'outside STATE_PATH'):
                self.prepare(state=state)
        self.assertFalse(self.backups.exists())
        self.assertFalse(self.keys.exists())

    def test_symlink_parent_is_rejected(self):
        destination = self.root / 'destination'
        destination.mkdir()
        self.backups.symlink_to(destination, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, 'symlinks'):
            self.prepare()
        self.assertEqual(list(destination.iterdir()), [])

    def test_insecure_key_permissions_and_shared_passwords_are_rejected(self):
        self.prepare()
        self.key.chmod(0o644)
        with self.assertRaisesRegex(ValueError, 'mode 600'):
            self.prepare()
        self.key.chmod(0o600)
        (self.keys / self.project / 'recovery.password').write_bytes(self.key.read_bytes())
        with self.assertRaisesRegex(ValueError, 'passwords must differ'):
            self.prepare()

    def test_failed_recovery_preflight_does_not_create_missing_content_key(self):
        self.keys.mkdir(mode=0o700)
        (self.keys / self.project).mkdir(mode=0o700)
        (self.keys / self.project / 'recovery.password').write_text('existing')
        (self.keys / self.project / 'recovery.password').chmod(0o644)
        with self.assertRaisesRegex(ValueError, 'mode 600'):
            self.prepare()
        self.assertFalse(self.key.exists())
        self.assertFalse(self.backups.exists())


@unittest.skipUnless(os.name == 'posix' and shutil.which('bash'), 'Requires Linux and Bash')
class BackupModeTests(unittest.TestCase):
    def source(self, config, backup_config, state):
        script = Path(__file__).with_name('restic-config.sh')
        return subprocess.run(['bash', '-eu', '-c', 'script=$1; shift; source "$script"', 'fixture', str(script)],
                              capture_output=True, text=True,
                              env=dict(os.environ, RH_CONFIG=str(config), RH_BACKUP_CONFIG=str(backup_config),
                                       PROJECT_NAME='rabbithole-hosted', STATE_PATH=str(state)))

    def test_missing_mode_defaults_to_local_without_reading_offserver_config(self):
        with tempfile.TemporaryDirectory() as temporary:
            config = Path(temporary) / 'hosted.env'
            for mode in ['', 'BACKUP_MODE=local\n', 'BACKUP_MODE="local"\n', "BACKUP_MODE='local'\n"]:
                config.write_text(mode)
                # Root deliberately overlaps every backup path, rejecting before any writes.
                result = self.source(config, Path(temporary) / 'absent', '/')
                self.assertNotEqual(result.returncode, 0)
                self.assertIn('outside STATE_PATH', result.stderr)

    def test_unknown_empty_duplicate_and_malformed_modes_fail_before_provisioning(self):
        script = Path(__file__).with_name('restic-config.sh')
        with tempfile.TemporaryDirectory() as temporary:
            config = Path(temporary) / 'hosted.env'
            for value in ['BACKUP_MODE=unknown', 'BACKUP_MODE=', "BACKUP_MODE='local", 'BACKUP_MODE=local\nBACKUP_MODE=offserver']:
                config.write_text(value + '\n')
                result = subprocess.run(['bash', '-eu', '-c', 'script=$1; shift; source "$script"', 'fixture', str(script)],
                                        capture_output=True, text=True, env=dict(os.environ, RH_CONFIG=str(config)))
                self.assertNotEqual(result.returncode, 0)
                self.assertIn('BACKUP_MODE must be local or offserver', result.stderr)

    def test_offserver_requires_private_configuration(self):
        script = Path(__file__).with_name('restic-config.sh')
        with tempfile.TemporaryDirectory() as temporary:
            config = Path(temporary) / 'hosted.env'
            config.write_text('BACKUP_MODE=offserver\n')
            result = subprocess.run(['bash', '-eu', '-c', 'script=$1; shift; source "$script"', 'fixture', str(script)],
                                    capture_output=True, text=True,
                                    env=dict(os.environ, RH_CONFIG=str(config), RH_BACKUP_CONFIG=str(Path(temporary) / 'absent')))
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('Off-server mode requires private backup.env', result.stderr)

    def test_offserver_validates_real_configuration_and_secret_paths(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            config = root / 'hosted.env'
            config.write_text('BACKUP_MODE="offserver"\n')
            content, recovery = root / 'content.password', root / 'recovery.password'
            for key, value in [(content, 'content fixture key'), (recovery, 'recovery fixture key')]:
                key.write_text(value)
                key.chmod(0o600)
            backup_config = root / 'backup.env'
            backup_config.write_text("RESTIC_REPOSITORY='sftp:fixture:/content'\n"
                                     "RECOVERY_REPOSITORY='sftp:fixture:/recovery'\n"
                                     f"RESTIC_PASSWORD_FILE='{content}'\nRECOVERY_PASSWORD_FILE='{recovery}'\n")
            backup_config.chmod(0o600)
            self.assertEqual(self.source(config, backup_config, root / 'state').returncode, 0)
            recovery.write_bytes(content.read_bytes())
            self.assertIn('different encryption password', self.source(config, backup_config, root / 'state').stderr)
            recovery.write_text('independent recovery key')
            recovery.chmod(0o644)
            self.assertIn('permissions 600', self.source(config, backup_config, root / 'state').stderr)
            recovery.chmod(0o600)
            self.assertIn('outside STATE_PATH', self.source(config, backup_config, root).stderr)
            backup_config.write_text(backup_config.read_text().replace('sftp:fixture:/content', str(root / 'local')))
            self.assertIn('two off-server encrypted destinations', self.source(config, backup_config, root / 'state').stderr)


if __name__ == '__main__':
    unittest.main()
