import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

scripts = Path(__file__).resolve().parents[2] / 'scripts'
sys.path.insert(0, str(scripts))
from admin_client import validate_base, NoRedirect, read_password_file, AdminClient
spec = importlib.util.spec_from_file_location('preservation', scripts / 'preservation-manifest.py')
preservation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preservation)


class MaintenanceBoundaries(unittest.TestCase):
    def test_explicit_password_file_is_bounded_and_preserves_the_exact_password(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder, 'password')
            path.write_bytes(b'  TEST FIXTURE password!  ')
            path.chmod(0o600)
            self.assertEqual('  TEST FIXTURE password!  ', read_password_file(path))
            for value in (b'', b'x' * 4097, b'fixture\n', b'fixture\r', b'fixture\0'):
                path.write_bytes(value)
                with self.assertRaises(ValueError):
                    read_password_file(path)

    @unittest.skipUnless(os.name == 'posix', 'Requires POSIX permissions and symlinks')
    def test_password_file_rejects_shared_permissions_links_and_nonfiles(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder, 'password'); path.write_text('TEST FIXTURE password')
            path.chmod(0o644)
            with self.assertRaises(ValueError):
                read_password_file(path)
            path.chmod(0o600)
            link = Path(folder, 'alias'); link.symlink_to(path)
            with self.assertRaises(ValueError):
                read_password_file(link)
            fifo = Path(folder, 'pipe'); os.mkfifo(fifo)
            with self.assertRaises(ValueError):
                read_password_file(fifo)

    def test_automated_login_uses_normal_csrf_login_and_logout_without_prompting(self):
        client = AdminClient('https://fixture.example/api', password_file='explicit-fixture-path')
        with (patch('admin_client.read_password_file', return_value='TEST FIXTURE password'),
              patch('admin_client.getpass.getpass', side_effect=AssertionError('Unexpected prompt')),
              patch.object(client, '_request', side_effect=[{'token': 'before'}, {}, {'token': 'after'}, None]) as calls):
            client.login()
            self.assertTrue(client.authenticated)
            self.assertEqual('after', client.token)
            self.assertEqual(('/auth/login', 'POST', {'password': 'TEST FIXTURE password'}), calls.call_args_list[1].args)
            client.logout()
            self.assertFalse(client.authenticated)
            self.assertEqual(('/auth/logout', 'POST'), calls.call_args.args)

    def test_remote_requires_https_without_embedded_credentials(self):
        for base in ('http://evil.example/api', 'https://user:secret@example.org/api',
                     'https://example.org/api?key=secret', 'https://example.org/other', 'file:///api'):
            with self.subTest(base=base), self.assertRaises(ValueError):
                validate_base(base)
        self.assertEqual('https://rabbithole.quintonloges.me/api', validate_base('https://rabbithole.quintonloges.me/api/'))
        self.assertEqual('http://127.0.0.1:47832/api', validate_base('http://127.0.0.1:47832/api'))

    def test_redirects_never_forward_credentials(self):
        with self.assertRaises(ValueError):
            NoRedirect().redirect_request(None, None, 307, '', {}, 'https://other.example/api')

    def test_preservation_detects_record_or_file_changes_and_accepts_additive_columns(self):
        schema = {'Notes': ['Id', 'Body'], 'Settings': ['Key', 'Value']}
        rows = {'Notes': [{'Id': 'fixture-note', 'Body': 'Unicode “private” note'}],
                'Settings': [{'Key': 'user', 'Value': '{}'}]}
        def run(command, **kwargs):
            sql = command[-1]
            value = schema if 'information_schema' in sql else rows['Notes' if '"Notes"' in sql else 'Settings']
            self.assertEqual('utf-8', kwargs['encoding'])
            return SimpleNamespace(stdout=json.dumps(value, ensure_ascii=False))
        with tempfile.TemporaryDirectory() as folder, patch.object(preservation.subprocess, 'run', side_effect=run):
            Path(folder, 'private.pdf').write_bytes(b'TEST FIXTURE file')
            before = preservation.capture(['psql'], folder)
            schema['Notes'].append('NewColumn')
            rows['Settings'].append({'Key': 'publication-upgrade', 'Value': 'done'})
            self.assertEqual(before, preservation.capture(['psql'], folder, before))
            rows['Notes'][0]['Body'] = 'changed'
            with self.assertRaisesRegex(ValueError, 'Notes'):
                preservation.capture(['psql'], folder, before)
            rows['Notes'][0]['Body'] = 'Unicode “private” note'
            Path(folder, 'private.pdf').write_bytes(b'changed')
            with self.assertRaisesRegex(ValueError, 'uploaded files'):
                preservation.capture(['psql'], folder, before)
