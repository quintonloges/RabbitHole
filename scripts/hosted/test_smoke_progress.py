"""Privacy checks remain observable and bounded while still rejecting leaks and drift."""
import copy
import importlib.util
import io
import json
from pathlib import Path
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
        self.args = SimpleNamespace(compose_prefix=str(prefix), output=str(self.output))
        self.private = [{'id': f'private-fixture-{n}', 'provider': 'local-upload', 'isPublic': False} for n in range(26)]
        self.baseline = {'items': self.private, 'notes': ['PRIVATE TEST FIXTURE NOTE'], 'exportedAt': 'before'}
        self.client = Mock()
        self.client.request.side_effect = [copy.deepcopy(self.baseline), dict(copy.deepcopy(self.baseline), exportedAt='after')]
        self.log = FlushedOutput()

    def response(self, command, **kwargs):
        self.assertEqual(45, kwargs['timeout'])
        self.assertIn('--max-time', command)
        path = command[-1].split('/api', 1)[1]
        if path.startswith('/public/items?'):
            return SimpleNamespace(stdout=json.dumps({'items': [{'id': 'public-fixture'}], 'total': 1})+'\n200')
        return SimpleNamespace(stdout='\n404' if path.startswith('/public/items/') else '\n401')

    def run_checks(self, response):
        with patch.object(smoke.subprocess, 'run', side_effect=response), patch('sys.stdout', self.log):
            smoke.check_privacy(self.client, self.args)

    def test_rate_limit_wait_is_announced_and_success_still_checks_every_private_record(self):
        calls = []
        def limited(command, **kwargs):
            calls.append(command[-1])
            if len(calls) == 10:
                return SimpleNamespace(stdout='\n429')
            return self.response(command, **kwargs)
        with patch.object(smoke.time, 'sleep') as sleep:
            self.run_checks(limited)
        sleep.assert_called_once_with(61)
        self.assertEqual(36, len(calls))  # Eight private routes, one page, 26 exclusions and one retry.
        self.assertIn('waiting 61 seconds', self.log.getvalue())
        self.assertIn('Checked 26 of 26 private records', self.log.getvalue())
        self.assertNotIn('PRIVATE TEST FIXTURE NOTE', self.log.getvalue())
        self.assertNotIn('private-fixture-', self.log.getvalue())
        self.assertGreater(self.log.flushes, 3)
        self.assertTrue(json.loads(self.output.read_text())['passed'])

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
