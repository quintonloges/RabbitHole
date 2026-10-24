import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

scripts = Path(__file__).resolve().parents[2] / 'scripts'
sys.path.insert(0, str(scripts))
from admin_client import validate_base, NoRedirect
spec = importlib.util.spec_from_file_location('preservation', scripts / 'preservation-manifest.py')
preservation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preservation)


class MaintenanceBoundaries(unittest.TestCase):
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
