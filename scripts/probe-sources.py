"""Read-only source research. No subscriptions or catalog writes; standard library only."""
import concurrent.futures
import datetime
import html
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import sys
import urllib.request
import urllib.parse
import urllib.robotparser
import xml.etree.ElementTree as ET


class FeedLinks(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'link' and attrs.get('type') in ('application/rss+xml', 'application/atom+xml'):
            self.links.append(attrs.get('href', ''))


def fetch(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'RabbitHole/1.0 (personal feed verification)'})
    with urllib.request.urlopen(request, timeout=25) as response:
        body = response.read(2_000_001)
        if len(body) > 2_000_000:
            raise ValueError('Response exceeds 2 MB research limit')
        return body, response.url, response.headers.get_content_type()


def plain(text):
    return re.sub(r'\s+', ' ', html.unescape(re.sub(r'<[^>]+>', ' ', text or ''))).strip()


def parse_feed(body):
    if b'<!DOCTYPE' in body.upper() or b'<!ENTITY' in body.upper():
        raise ValueError('DTD/entity not accepted')
    root = ET.fromstring(body)
    local = lambda tag: tag.split('}')[-1]
    if local(root.tag) not in ('rss', 'feed', 'RDF'):
        raise ValueError('Not an RSS/Atom document')
    records = []
    for entry in root.iter():
        if local(entry.tag) not in ('entry', 'item'):
            continue
        fields = {}
        link = None
        for child in entry:
            key = local(child.tag)
            value = ''.join(child.itertext()).strip()
            fields.setdefault(key, value)
            if key == 'link' and child.get('rel', 'alternate') == 'alternate':
                link = child.get('href') or value
        records.append({'title': plain(fields.get('title')), 'url': link,
                        'author': plain(fields.get('creator') or fields.get('author')),
                        'date': fields.get('pubDate') or fields.get('published') or fields.get('date') or fields.get('updated'),
                        'excerpt': plain(fields.get('description') or fields.get('summary') or fields.get('content'))[:900]})
    return records[:100]


def probe(source):
    result = dict(source)
    result['checkedAt'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    try:
        url = source.get('url') or source['website']
        if not source.get('url'):
            robots = urllib.robotparser.RobotFileParser(urllib.parse.urljoin(url, '/robots.txt'))
            try:
                raw, _, _ = fetch(robots.url)
                robots.parse(raw.decode('utf-8', errors='replace').splitlines())
                if not robots.can_fetch('RabbitHole', url):
                    raise ValueError('Homepage metadata disallowed by robots')
            except urllib.error.HTTPError as error:
                if error.code != 404:
                    raise
        body, final, mime = fetch(url)
        if mime in ('text/html', 'application/xhtml+xml'):
            links = FeedLinks()
            links.feed(body.decode('utf-8', errors='replace'))
            result['discovered'] = [urllib.parse.urljoin(final, link) for link in links.links]
            if not result['discovered']:
                raise ValueError('No advertised RSS/Atom feed')
            # Main publication feed precedes comments and section feeds on most sites.
            url = next((link for link in result['discovered'] if 'comments' not in link), result['discovered'][0])
            body, final, mime = fetch(url)
        records = parse_feed(body)
        result.update(status='parsed' if records else 'empty', verifiedUrl=final, mime=mime,
                      count=len(records), records=records)
    except Exception as error:
        result.update(status='failed', error=str(error))
    return result


if __name__ == '__main__':
    path = Path(sys.argv[1])
    output = Path(sys.argv[2])
    sources = json.loads(path.read_text(encoding='utf-8-sig'))
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
        results = list(executor.map(probe, sources))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    for source in results:
        print(json.dumps({k: source[k] for k in ('name', 'status', 'verifiedUrl', 'count', 'error') if k in source}, ensure_ascii=False))
