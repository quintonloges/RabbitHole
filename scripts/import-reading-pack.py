"""Opt-in, repeatable import of individually curated real links/research records."""
import argparse
import importlib.util
import json
from pathlib import Path
import urllib.parse

spec = importlib.util.spec_from_file_location('source_pack', Path(__file__).with_name('apply-source-pack.py'))
api = importlib.util.module_from_spec(spec)
spec.loader.exec_module(api)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pack', default='config/curated-reading-pack.json')
    parser.add_argument('--base', default='http://127.0.0.1:3000/api')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--report', default='data/curation/reading-pack-run.json')
    args = parser.parse_args()
    target = urllib.parse.urlsplit(args.base)
    if target.scheme != 'http' or target.hostname not in ('127.0.0.1', 'localhost', '::1'):
        parser.error('Only the local HTTP RabbitHole instance is supported.')
    pack = json.loads(Path(args.pack).read_text(encoding='utf-8-sig'))
    topics = {t['name']: t['id'] for t in api.request(args.base, '/topics')}
    # Paginated item reads avoid exporting private notes/documents just to detect duplicate URLs.
    items = []
    offset = 0
    while True:
        page = api.request(args.base, f'/items?limit=100&offset={offset}')
        items.extend(page['items'])
        if len(items) >= page['total']:
            break
        offset += 100
    results = []
    for reading in pack['items']:
        existing = next((i for i in items if i['canonicalUrl'].rstrip('/') == reading.get('url', '').rstrip('/')
                        or (reading.get('providerId') and i['provider'] == reading['provider'] and i['providerId'] == reading['providerId'])), None)
        result = {'label': reading['label']}
        try:
            if existing:
                result.update(action='preserved existing', itemId=existing['id'])
            elif not args.apply:
                result['action'] = 'import on --apply'
            else:
                topic_ids = [topics[name] for name in reading['topics']]
                if reading.get('providerId'):
                    item = api.request(args.base, '/research/import', 'POST', {k: reading[k] for k in ('provider', 'providerId')})
                else:
                    item = api.request(args.base, '/items/import-url', 'POST', {'url': reading['url'], 'title': reading['label'], 'feedUrl': reading.get('feedUrl')})
                # The title is a verified source title, never generated source text. Explicit tags
                # apply only to records created by this run, preserving pre-existing choices.
                if item['id'] not in {i['id'] for i in items}:
                    metadata = {'topicIds': sorted(set(item['topicIds'] + topic_ids))}
                    if reading.get('verifiedAuthor') and not item.get('author'):
                        metadata['author'] = reading['verifiedAuthor']
                    item = api.request(args.base, '/items/' + item['id'], 'PATCH', metadata)
                items.append(item)
                result.update(action='imported', itemId=item['id'], title=item['title'], author=item.get('author'),
                              publishedAt=item.get('publishedAt'), warning=item.get('importWarning'), kind=item['kind'])
        except Exception as error:
            result.update(action='failed', error=str(error))
        results.append(result)
        print(json.dumps(result, ensure_ascii=False), flush=True)
        output = Path(args.report)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    return 1 if any('error' in result for result in results) else 0


if __name__ == '__main__':
    raise SystemExit(main())
