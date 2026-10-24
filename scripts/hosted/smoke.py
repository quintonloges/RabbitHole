"""Authenticated, read-only launch checks. Run on the host with public maintenance active.

Use --base https://rabbithole.quintonloges.me/api. Password is prompted. Public API probes run
inside the API container so the public maintenance response can remain in place.
"""
import argparse
import json
from pathlib import Path
import subprocess
import sys
import time
import urllib.error
import urllib.request

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from admin_client import AdminClient


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--compose-prefix', required=True, help='Path to a JSON array of local docker compose arguments; no secrets.')
    args = parser.parse_args()
    client = AdminClient(args.base)
    client.login()
    prefix = json.loads(Path(args.compose_prefix).read_text())
    def public(path, expected=200):
        for attempt in range(4):
            result = subprocess.run([*prefix, 'exec', '-T', 'api', 'curl', '--silent', '--show-error',
                                     '--output', '-', '--write-out', '\n%{http_code}', 'http://localhost:8080/api' + path],
                                    check=True, capture_output=True, text=True)
            body, status = result.stdout.rsplit('\n', 1)
            if int(status) != 429 or attempt == 3:
                break
            time.sleep(61)
        if int(status) != expected:
            raise AssertionError(f'{path}: expected {expected}, got {status}')
        return json.loads(body) if expected == 200 else None

    for path in ('/items', '/topics', '/settings', '/holes', '/library/books', '/practice/prompts', '/ai/status', '/system'):
        public(path, 401)
    before = client.request('/export', 'POST')
    page = public('/public/items?pageSize=100')
    visible = set()
    allowed = {'id', 'title', 'canonicalUrl', 'author', 'publishedAt', 'ingestedAt', 'type', 'provider', 'sourceName', 'excerpt', 'topics', 'embedUrl', 'imageUrl'}
    number = 1
    while True:
        for item in page['items']:
            assert set(item).issubset(allowed), 'Unapproved public fields'
            assert item['id'] not in visible, 'Duplicate public IDs'
            visible.add(item['id'])
        if len(visible) >= page['total']:
            break
        number += 1
        page = public(f'/public/items?pageSize=100&page={number}')
        assert page['items'], 'Pagination stopped early'
    for item in before['items']:
        if not item.get('isPublic') or item.get('documentId') or item.get('provider') == 'local-upload':
            public('/public/items/' + item['id'], 404)
            assert item['id'] not in visible, 'Private item surfaced'
    after = client.request('/export', 'POST')
    before.pop('exportedAt', None)
    after.pop('exportedAt', None)
    assert before == after, 'Read-only anonymous browsing changed personal data'
    client.request('/auth/logout', 'POST')
    Path(args.output).write_text(json.dumps({'passed': True, 'publicItems': len(visible)}, indent=2))
    print(f'PASS: authenticated access, anonymous legacy denial, {len(visible)} safe public records, private exclusions, no state changes.')


if __name__ == '__main__':
    main()
