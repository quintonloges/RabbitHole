"""Privacy checks remain observable and bounded while still rejecting leaks and drift."""
import copy
import importlib.util
import io
import json
from pathlib import Path
import sqlite3
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

spec = importlib.util.spec_from_file_location('hosted_smoke', Path(__file__).with_name('smoke.py'))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)


class FlushedOutput(io.StringIO):
    flushes = 0

    def flush(self):
        self.flushes += 1
        super().flush()


class SmokeProgressTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        root = Path(self.temporary.name)
        prefix = root/'compose.json'; prefix.write_text('["fixture-compose"]')
        self.output = root/'verified.json'
        self.args = SimpleNamespace(compose_prefix=str(prefix), output=str(self.output), all_private_details=False)
        self.private = [{'id': f'private-fixture-{n}', 'provider': 'local-upload', 'isPublic': False} for n in range(433)]
        self.private += [{'id': 'hidden-fixture', 'isPublic': False},
                         {'id': 'alias-fixture', 'isPublic': False, 'mergedIntoId': 'public-fixture'}]
        self.baseline = {'items': self.private + [{'id': 'public-fixture', 'isPublic': True}],
                         'sections': [], 'notes': ['PRIVATE TEST FIXTURE NOTE'], 'exportedAt': 'before'}
        self.client = Mock()
        self.client.request.side_effect = [copy.deepcopy(self.baseline), dict(copy.deepcopy(self.baseline), exportedAt='after')]
        self.log = FlushedOutput()
        self.index = sqlite3.connect(':memory:')
        self.addCleanup(self.index.close)
        self.index.execute('CREATE TABLE "PublicCatalogEntries" ("Id" TEXT, "IsPublic" BOOLEAN, "MergedIntoId" TEXT)')
        self.index.executemany('INSERT INTO "PublicCatalogEntries" VALUES (?, ?, ?)',
                               [('public-fixture', True, None), ('hidden-fixture', False, None),
                                ('alias-fixture', False, 'public-fixture')])
        self.requests = []
        self.index_reads = 0

    def response(self, command, **kwargs):
        self.assertEqual(45, kwargs['timeout'])
        self.assertTrue(kwargs['check'])
        if 'psql' in command:
            self.index_reads += 1
            rows = self.index.execute(command[-1]).fetchall()
            return SimpleNamespace(stdout=''.join(row[0] + '\n' for row in rows))
        self.assertIn('--max-time', command)
        path = command[-1].split('/api', 1)[1]
        self.requests.append(path)
        if path.startswith('/public/items?'):
            return SimpleNamespace(stdout=json.dumps({'items': [{'id': 'public-fixture'}], 'total': 1})+'\n200')
        if path == '/public/items/public-fixture':
            return SimpleNamespace(stdout='{"id":"public-fixture"}\n200')
        return SimpleNamespace(stdout='\n404' if path.startswith('/public/items/') else '\n401')

    def run_checks(self, response):
        with patch.object(smoke.subprocess, 'run', side_effect=response), patch('sys.stdout', self.log):
            smoke.check_privacy(self.client, self.args)

    def test_rate_limit_wait_is_announced_and_success_still_checks_every_private_record(self):
        throttled = False
        def limited(command, **kwargs):
            nonlocal throttled
            if not throttled and command[-1].endswith('/public/items/private-fixture-0'):
                throttled = True
                return SimpleNamespace(stdout='\n429')
            return self.response(command, **kwargs)
        with patch.object(smoke.time, 'sleep') as sleep:
            self.run_checks(limited)
        sleep.assert_called_once_with(61)
        self.assertEqual(1, self.index_reads)
        self.assertEqual(['/public/items/private-fixture-0', '/public/items/hidden-fixture', '/public/items/alias-fixture',
                          '/public/items/public-fixture'], [p for p in self.requests if p.startswith('/public/items/')])
        self.assertIn('waiting 61 seconds', self.log.getvalue())
        self.assertIn('Checked 435 private IDs', self.log.getvalue())
        self.assertNotIn('PRIVATE TEST FIXTURE NOTE', self.log.getvalue())
        self.assertNotIn('private-fixture-', self.log.getvalue())
        self.assertGreater(self.log.flushes, 3)
        self.assertEqual({'passed': True, 'publicItems': 1, 'privateItemsChecked': 435, 'privateDetailProbes': 3},
                         json.loads(self.output.read_text()))

    def test_unsampled_private_id_on_later_public_page_blocks_launch(self):
        def leaked(command, **kwargs):
            if command[-1].endswith('/public/items?pageSize=100'):
                return SimpleNamespace(stdout='{"items":[{"id":"public-fixture"}],"total":2}\n200')
            if command[-1].endswith('/public/items?pageSize=100&page=2'):
                return SimpleNamespace(stdout='{"items":[{"id":"private-fixture-432"}],"total":2}\n200')
            return self.response(command, **kwargs)
        with self.assertRaisesRegex(AssertionError, 'surfaced in the public catalog'):
            self.run_checks(leaked)
        self.assertFalse(self.output.exists())

    def test_private_alias_marked_public_in_detail_index_blocks_launch_even_when_unlisted(self):
        self.index.execute('UPDATE "PublicCatalogEntries" SET "IsPublic" = 1 WHERE "Id" = ?', ('alias-fixture',))
        with self.assertRaisesRegex(AssertionError, 'marked public in the detail index'):
            self.run_checks(self.response)
        self.assertFalse(self.output.exists())

    def test_published_section_reference_still_counts_as_private(self):
        baseline = dict(self.baseline, sections=[{'itemId': 'public-fixture'}])
        self.client.request.side_effect = [baseline]
        with self.assertRaisesRegex(AssertionError, 'surfaced in the public catalog'):
            self.run_checks(self.response)
        self.assertFalse(self.output.exists())

    def test_broken_public_detail_route_blocks_launch(self):
        def missing(command, **kwargs):
            if command[-1].endswith('/public/items/public-fixture'):
                return SimpleNamespace(stdout='\n404')
            return self.response(command, **kwargs)
        with self.assertRaisesRegex(AssertionError, 'expected 200, got 404'):
            self.run_checks(missing)
        self.assertFalse(self.output.exists())

    def test_index_failure_blocks_launch(self):
        def unavailable(command, **kwargs):
            if 'psql' in command:
                raise subprocess.CalledProcessError(1, command)
            return self.response(command, **kwargs)
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_checks(unavailable)
        self.assertFalse(self.output.exists())

    def test_full_audit_probes_an_unsampled_detail(self):
        self.args.all_private_details = True
        def leaked(command, **kwargs):
            if command[-1].endswith('/public/items/private-fixture-432'):
                return SimpleNamespace(stdout='{}\n200')
            return self.response(command, **kwargs)
        with self.assertRaisesRegex(AssertionError, 'expected 404, got 200'):
            self.run_checks(leaked)
        self.assertFalse(self.output.exists())

    def test_hung_probe_exits_without_publishing_success(self):
        with self.assertRaises(subprocess.TimeoutExpired):
            self.run_checks(lambda command, **kwargs: (_ for _ in ()).throw(subprocess.TimeoutExpired(command, 45)))
        self.assertFalse(self.output.exists())

    def test_publicly_visible_private_record_still_blocks_launch(self):
        def leaked(command, **kwargs):
            if '/public/items/private-fixture-' in command[-1]:
                return SimpleNamespace(stdout='{}\n200')
            return self.response(command, **kwargs)
        with self.assertRaises(AssertionError):
            self.run_checks(leaked)
        self.assertFalse(self.output.exists())

    def test_personal_state_drift_still_blocks_launch(self):
        self.client.request.side_effect = [copy.deepcopy(self.baseline), dict(copy.deepcopy(self.baseline), notes=['changed'])]
        with self.assertRaisesRegex(AssertionError, 'changed personal data'):
            self.run_checks(self.response)
        self.assertFalse(self.output.exists())


if __name__ == '__main__':
    unittest.main()
