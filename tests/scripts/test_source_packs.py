"""Installer regressions with an isolated API boundary; no network or catalog writes."""
import contextlib
import copy
import importlib.util
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch


def load_script(name):
    path = Path(__file__).resolve().parents[2] / 'scripts' / (name + '.py')
    spec = importlib.util.spec_from_file_location(name.replace('-', '_'), path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


reading_pack = load_script('import-reading-pack')
source_pack = load_script('apply-source-pack')


def run_pack(module, pack, request, *arguments):
    api = getattr(module, 'api', module)
    output = io.StringIO()
    with (patch('sys.argv', [module.__file__, *arguments]),
          patch.object(api, 'request', side_effect=request) as calls,
          patch.object(Path, 'read_text', return_value=json.dumps(pack)),
          patch.object(Path, 'mkdir'), patch.object(Path, 'write_text') as report,
          contextlib.redirect_stdout(output)):
        code = module.main()
    return code, json.loads(report.call_args.args[0]), calls.call_args_list, output.getvalue()


class ReadingPackTests(unittest.TestCase):
    def setUp(self):
        self.item = {
            'id': 'existing-item', 'canonicalUrl': 'https://example.org/paper',
            'provider': 'manual', 'providerId': None, 'metadataJson': None,
            'title': 'TEST FIXTURE user title', 'author': 'TEST FIXTURE corrected author',
            'topicIds': ['personal-topic'], 'kind': 'paper',
        }
        self.reading = {'label': 'TEST FIXTURE source title', 'provider': 'openalex',
                        'providerId': 'W42', 'topics': []}

    def catalog(self, base, path, method='GET', body=None):
        if method == 'GET' and path == '/topics':
            return []
        if method == 'GET' and path == '/items?limit=100&offset=0':
            return {'items': [self.item], 'total': 1}
        raise AssertionError('Unexpected catalog mutation: ' + method + ' ' + path)

    def test_known_provider_aliases_preserve_existing_item_without_import_or_patch(self):
        aliases = [
            ('openalex', 'W42', {'provider': 'openalex', 'providerId': 'W42'}),
            ('openalex', 'W42', {'metadataJson': json.dumps({'providerReferences': [
                None, {'provider': 'rss', 'id': 'unrelated'}, {'provider': 'openalex', 'id': 'W42'}]})}),
            ('openalex', 'W42', {'metadataJson': json.dumps({'openAlexId': 'W42'})}),
            ('arxiv', '1706.03762', {'arxivId': '1706.03762'}),
            ('arxiv', '1706.03762', {'metadataJson': json.dumps({'arxivId': '1706.03762'})}),
        ]
        for provider, provider_id, fields in aliases:
            for arguments in ((), ('--apply',)):
                with self.subTest(provider=provider, fields=fields, arguments=arguments):
                    self.setUp()
                    self.item.update(fields)
                    original = copy.deepcopy(self.item)
                    self.reading.update(provider=provider, providerId=provider_id)
                    code, report, calls, _ = run_pack(reading_pack, {'items': [self.reading]},
                                                     self.catalog, *arguments)
                    self.assertEqual(0, code)
                    self.assertEqual('preserved existing', report[0]['action'])
                    self.assertEqual(self.item['id'], report[0]['itemId'])
                    self.assertEqual(original, self.item)
                    self.assertEqual(2, len(calls))

    def test_existing_url_is_preserved_with_a_trailing_slash(self):
        reading = {'label': 'TEST FIXTURE title', 'url': self.item['canonicalUrl'] + '/', 'topics': []}
        code, report, calls, _ = run_pack(reading_pack, {'items': [reading]}, self.catalog, '--apply')
        self.assertEqual(0, code)
        self.assertEqual('preserved existing', report[0]['action'])
        self.assertEqual(2, len(calls))

    def test_malformed_metadata_and_other_provider_ids_do_not_match(self):
        for metadata in (None, '{broken', 'null', '[]', '42',
                         '{"providerReferences": {"provider": "openalex", "id": "W42"}}',
                         '{"providerReferences": [null, 7, {"provider": "rss", "id": "W42"}]}'):
            with self.subTest(metadata=metadata):
                self.item['metadataJson'] = metadata
                code, report, calls, _ = run_pack(reading_pack, {'items': [self.reading]}, self.catalog)
                self.assertEqual(0, code)
                self.assertEqual('import on --apply', report[0]['action'])
                self.assertEqual(2, len(calls))

    def test_missing_record_imports_once_and_repeat_selection_is_preserved(self):
        created = dict(self.item, id='new-item', provider='openalex', providerId='W42', topicIds=[])

        def request(base, path, method='GET', body=None):
            if method == 'POST' and path == '/research/import':
                return copy.deepcopy(created)
            if method == 'PATCH' and path == '/items/new-item':
                self.assertEqual({'topicIds': []}, body)
                return copy.deepcopy(created)
            return self.catalog(base, path, method, body)

        original = copy.deepcopy(self.item)
        code, report, calls, _ = run_pack(reading_pack, {'items': [self.reading, self.reading]},
                                         request, '--apply')
        self.assertEqual(0, code)
        self.assertEqual(['imported', 'preserved existing'], [row['action'] for row in report])
        self.assertEqual(4, len(calls))
        self.assertEqual(original, self.item)


class SourcePackTests(unittest.TestCase):
    def setUp(self):
        self.source = {'id': 'existing-source', 'name': 'TEST FIXTURE subscription',
                       'kind': 'rss', 'url': 'https://example.org/feed', 'query': '',
                       'enabledDefault': True, 'enabled': False, 'topics': [],
                       'intervalMinutes': 1440, 'resultLimit': 8}

    def request_with_job(self, status):
        def request(base, path, method='GET', body=None):
            if method == 'GET' and path == '/topics':
                return []
            if method == 'GET' and path == '/sources':
                return [self.source]
            if method == 'POST' and path == '/sources/existing-source/refresh':
                return {'status': status, 'error': None if status == 'completed' else
                        'TEST FIXTURE remote HTTP 503', 'imported': 0}
            raise AssertionError('Unexpected settings mutation: ' + method + ' ' + path)
        return request

    def test_failed_existing_refresh_is_visible_and_returns_nonzero(self):
        for status in ('failed', 'interrupted'):
            with self.subTest(status=status):
                original = copy.deepcopy(self.source)
                code, report, calls, output = run_pack(source_pack, {'sources': [self.source]},
                    self.request_with_job(status), '--apply', '--refresh')
                self.assertEqual(1, code)
                self.assertEqual(status, report[0]['job']['status'])
                self.assertIn('HTTP 503', report[0]['error'])
                self.assertIn('HTTP 503', output)
                self.assertEqual(original, self.source)
                self.assertEqual(3, len(calls))

    def test_completed_existing_refresh_with_zero_new_records_succeeds(self):
        code, report, calls, _ = run_pack(source_pack, {'sources': [self.source]},
            self.request_with_job('completed'), '--apply', '--refresh')
        self.assertEqual(0, code)
        self.assertNotIn('error', report[0])
        self.assertEqual('completed', report[0]['job']['status'])
        self.assertEqual(3, len(calls))

    def test_existing_source_is_not_refreshed_without_both_flags(self):
        for arguments in ((), ('--refresh',), ('--apply',)):
            with self.subTest(arguments=arguments):
                code, report, calls, _ = run_pack(source_pack, {'sources': [self.source]},
                    self.request_with_job('failed'), *arguments)
                self.assertEqual(0, code)
                self.assertNotIn('job', report[0])
                self.assertEqual(2, len(calls))


if __name__ == '__main__':
    unittest.main()
