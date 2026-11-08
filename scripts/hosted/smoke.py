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
    parser.add_argument('--all-private-details', action='store_true', help='Also request every private detail URL during a manual audit.')
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


def check_private_exclusions(baseline, visible, prefix, public, all_details):
    section_items = {section['itemId'] for section in baseline['sections']}
    private_ids = set()
    samples = {}
    for item in baseline['items']:
        item_id = item['id']
        uploaded = (item.get('documentId') or item.get('sectionId') or item_id in section_items
                    or item.get('provider') == 'local-upload')
        if item.get('isPublic') and not uploaded:
            continue
        private_ids.add(item_id)
        category = 'upload' if uploaded else 'hidden'
        if item.get('mergedIntoId'):
            category = 'alias'
        samples.setdefault(category, item_id)

    if not private_ids.isdisjoint(visible):
        raise AssertionError('Private item surfaced in the public catalog')
    # Detail lookups require a published index row for the requested ID, including merged IDs omitted from lists.
    query = 'SELECT "Id" FROM "PublicCatalogEntries" WHERE "IsPublic"'
    result = subprocess.run([*prefix, 'exec', '-T', 'db', 'psql', '-XAt', '-v', 'ON_ERROR_STOP=1',
                             '-U', 'rabbithole', '-d', 'rabbithole', '-c', query],
                            check=True, capture_output=True, text=True, timeout=45)
    indexed = set(result.stdout.splitlines())
    if not private_ids.isdisjoint(indexed):
        raise AssertionError('Private item marked public in the detail index')
    if not visible.issubset(indexed):
        raise AssertionError('Public catalog contains an item missing from the detail index')
    probes = sorted(private_ids) if all_details else list(samples.values())
    print(f'Checked {len(private_ids)} private IDs against the catalog and detail index. '
          f'Probing {len(probes)} private detail URLs.', flush=True)
    for number, item_id in enumerate(probes, 1):
        try:
            public('/public/items/' + item_id, 404)
        except subprocess.SubprocessError as error:
            raise RuntimeError(f'Private detail request failed ({type(error).__name__}).') from None
        if all_details and number % 25 == 0:
            print(f'Checked {number} of {len(probes)} private detail URLs.', flush=True)
    return len(private_ids), len(probes)


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
            route = '/public/items/{id}' if path.startswith('/public/items/') else path
            raise AssertionError(f'{route}: expected {expected}, got {status}')
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
    private_count, detail_probes = check_private_exclusions(before, visible, prefix, public, args.all_private_details)
    if visible:
        public_id = min(visible)
        detail = public('/public/items/' + public_id)
        if detail.get('id') != public_id or not set(detail).issubset(allowed):
            raise AssertionError('Public detail did not return the expected public record')
    print('Verifying that anonymous browsing did not change personal state.', flush=True)
    after = client.request('/export', 'POST')
    before.pop('exportedAt', None)
    after.pop('exportedAt', None)
    assert before == after, 'Read-only anonymous browsing changed personal data'
    Path(args.output).write_text(json.dumps({'passed': True, 'publicItems': len(visible),
                                           'privateItemsChecked': private_count, 'privateDetailProbes': detail_probes}, indent=2))
    print(f'PASS: authenticated access, anonymous legacy denial, {len(visible)} safe public records, private exclusions, no state changes.', flush=True)


if __name__ == '__main__':
    main()
