"""Authenticated, read-only launch checks. Run on the host with public maintenance active.

Use --base https://rabbithole.quintonloges.me/api. Password is prompted or supplied via a private file. Public API probes run
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
    parser.add_argument('--output')
    parser.add_argument('--compose-prefix', help='Path to a JSON array of local docker compose arguments; no secrets.')
    parser.add_argument('--password-file', help='Explicit owner-only temporary file containing the existing admin password.')
    parser.add_argument('--check-login-only', action='store_true', help='Verify real HTTPS cookie/CSRF login before maintenance.')
    args = parser.parse_args()
    if not args.check_login_only and (not args.output or not args.compose_prefix):
        parser.error('--output and --compose-prefix are required for full privacy checks.')
    client = AdminClient(args.base, password_file=args.password_file)
    try:
        print('Checking administrator login and session.', flush=True)
        client.login()
        assert client.request('/auth/session')['authenticated'] is True, 'Administrator session was not established'
        if args.check_login_only:
            print('PASS: existing administrator credentials authenticated over HTTPS with CSRF.', flush=True)
            return
        check_privacy(client, args)
    finally:
        client.logout()


def check_privacy(client, args):
    prefix = json.loads(Path(args.compose_prefix).read_text())
    def public(path, expected=200):
        for attempt in range(4):
            result = subprocess.run([*prefix, 'exec', '-T', 'api', 'curl', '--silent', '--show-error', '--connect-timeout', '10', '--max-time', '30',
                                     '--output', '-', '--write-out', '\n%{http_code}', 'http://localhost:8080/api' + path],
                                    check=True, capture_output=True, text=True, timeout=45)
            body, status = result.stdout.rsplit('\n', 1)
            if int(status) != 429 or attempt == 3:
                break
            print(f'Privacy probe rate-limited; waiting 61 seconds before retry {attempt + 2}/4.', flush=True)
            time.sleep(61)
        if int(status) != expected:
            raise AssertionError(f'{path}: expected {expected}, got {status}')
        return json.loads(body) if expected == 200 else None

    print('Checking anonymous rejection on private endpoints.', flush=True)
    for path in ('/items', '/topics', '/settings', '/holes', '/library/books', '/practice/prompts', '/ai/status', '/system'):
        public(path, 401)
    print('Capturing personal-state baseline and checking public catalog pages.', flush=True)
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
        print(f'Checked public page {number}: {len(visible)} of {page["total"]} records.', flush=True)
        if len(visible) >= page['total']:
            break
        number += 1
        page = public(f'/public/items?pageSize=100&page={number}')
        assert page['items'], 'Pagination stopped early'
    private = [item for item in before['items'] if not item.get('isPublic') or item.get('documentId') or item.get('provider') == 'local-upload']
    print(f'Checking exclusion of {len(private)} private records.', flush=True)
    for index, item in enumerate(private, 1):
        public('/public/items/' + item['id'], 404)
        assert item['id'] not in visible, 'Private item surfaced'
        if index % 25 == 0 or index == len(private):
            print(f'Checked {index} of {len(private)} private records.', flush=True)
    print('Verifying that anonymous browsing did not change personal state.', flush=True)
    after = client.request('/export', 'POST')
    before.pop('exportedAt', None)
    after.pop('exportedAt', None)
    assert before == after, 'Read-only anonymous browsing changed personal data'
    Path(args.output).write_text(json.dumps({'passed': True, 'publicItems': len(visible)}, indent=2))
    print(f'PASS: authenticated access, anonymous legacy denial, {len(visible)} safe public records, private exclusions, no state changes.', flush=True)


if __name__ == '__main__':
    main()
