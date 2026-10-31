"""Opt-in source pack installer using RabbitHole's existing source APIs.

Default: read-only plan. --apply verifies, imports, then activates new subscriptions.
Existing subscriptions and all user preferences/state are preserved. --refresh explicitly
refreshes existing matching subscriptions as well, without changing their settings.
"""
import argparse
import datetime
import json
from pathlib import Path
import sys
import urllib.parse
import urllib.request
sys.path.insert(0, str(Path(__file__).resolve().parent))
from admin_client import request, validate_base, use_password_file, logout


def identity(source):
    url = urllib.parse.urlsplit(source.get('url', '').strip())
    canonical = urllib.parse.urlunsplit((url.scheme.lower(), url.netloc.lower(), url.path.rstrip('/'), url.query, ''))
    return source['kind'], canonical, source.get('query', '').strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pack', default='config/curated-source-pack.json')
    parser.add_argument('--base', default='http://127.0.0.1:47832/api')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--refresh', action='store_true')
    parser.add_argument('--only', help='Only a named source; exact match')
    parser.add_argument('--report', default='data/curation/source-pack-run.json')
    parser.add_argument('--password-file', help='Explicit private temporary password file for approved automated releases.')
    args = parser.parse_args()
    try:
        validate_base(args.base)
    except ValueError as error:
        parser.error(str(error))
    if args.password_file:
        use_password_file(args.base, args.password_file)
    try:
        return apply_pack(args)
    finally:
        if args.password_file:
            logout(args.base)


def apply_pack(args):
    pack = json.loads(Path(args.pack).read_text(encoding='utf-8-sig'))
    topics = {t['name']: t['id'] for t in request(args.base, '/topics')}
    sources = {identity(s): s for s in request(args.base, '/sources')}
    results = []
    for selected in pack['sources']:
        if not selected['enabledDefault'] or (args.only and args.only != selected['name']):
            continue
        existing = sources.get(identity(selected))
        report = {'name': selected['name'], 'at': datetime.datetime.now(datetime.timezone.utc).isoformat()}
        try:
            if not args.apply:
                report['action'] = 'preserve existing' if existing else 'verify and add on --apply'
            elif existing:
                report.update(action='preserved existing settings', sourceId=existing['id'])
                if args.refresh:
                    report['job'] = request(args.base, '/sources/' + existing['id'] + '/refresh', 'POST')
                    if report['job']['status'] != 'completed':
                        raise ValueError('Refresh failed: ' + str(report['job'].get('error') or report['job']['status']))
            else:
                missing = [name for name in selected['topics'] if name not in topics]
                if missing:
                    raise ValueError('Missing topics: ' + ', '.join(missing) + '; create/edit them in Settings first.')
                body = {k: selected[k] for k in ('name', 'kind', 'url', 'query', 'intervalMinutes', 'resultLimit')}
                body.update(enabled=False, topicIds=[topics[name] for name in selected['topics']])
                source = request(args.base, '/sources', 'POST', body)
                sources[identity(selected)] = source
                report['sourceId'] = source['id']
                test = request(args.base, '/sources/' + source['id'] + '/test', 'POST')
                report['verification'] = test
                if not test.get('ok') or not test.get('count'):
                    raise ValueError('No parseable records; left disabled for inspection.')
                job = request(args.base, '/sources/' + source['id'] + '/refresh', 'POST')
                report['job'] = job
                if job['status'] != 'completed':
                    raise ValueError('Refresh failed; source left disabled: ' + str(job.get('error')))
                activated = request(args.base, '/sources/' + source['id'], 'PATCH', {'enabled': True})
                report.update(action='imported and activated', imported=job['imported'], nextSyncAt=activated['nextSyncAt'])
        except Exception as error:
            report.update(action='failed; no settings overwritten', error=str(error))
        results.append(report)
        print(json.dumps({k: v for k, v in report.items() if k not in ('verification', 'job')}, ensure_ascii=False), flush=True)
        output = Path(args.report)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    return 1 if any('error' in result for result in results) else 0


if __name__ == '__main__':
    sys.exit(main())
