# Source expansion and older web discovery

Implemented 31 October 2026. The changes are built in development; hosted release and subscription activation are separate operations.

## What changes in discovery

The public home defaults to **Daily mix**. About every fourth row is a topic-matched older item when available, with a different stable rotation each UTC day. **Newest added** and Search keep their chronological import ordering. Private daily editions reserve 25% for relevant archive material by default, adjustable from 0–50% in **Settings → Daily picks → From the archive**. Existing daily selections remain stable; use **New selection** to apply a changed preference immediately.

Ordinary archive candidates need a known publication date at least 180 days old and explanatory/research/other lasting-format cues. Older-web candidates have already matched a configured topic search in historical submissions and may have an unknown original date. Recognizable announcements, releases, sales, events and roundups do not receive archive slots. Private selections also preserve interest, read/dismiss, source and explicit-choice rules. Public selections use public metadata and topics only. These are metadata heuristics, not a guarantee that an old tutorial remains technically current.

Older headlines append `(year)` using the original source's publication date; the byline shows the full date. An absent date stays **Date unknown**. Ingestion time, Atom `updated`, HN submission dates, HN submitter names and an unverified year in an HN title never substitute for original publication/author metadata. Existing stored dates are not rewritten by guesswork.

## Newly verified feeds

Live verification used RabbitHole's actual safe fetcher and feed parser on 31 October 2026, without importing into personal databases. Counts describe the fetched feed, before configured import limits; they can change.

| Feed | Parsed entries | Coverage |
| --- | ---: | --- |
| [Julia Evans](https://jvns.ca/atom.xml) | 20 | Programming explanations; this sample supplies revision dates only, so publication dates remain unknown |
| [Bartosz Ciechanowski](https://ciechanow.ski/atom.xml) | 2 | Interactive engineering explanations |
| [James Somers](https://jsomers.net/blog/feed) | 10 | Essays and software |
| [Sean Carroll](https://preposterousuniverse.com/blog/feed/) | 10 | Physics, philosophy and conversations |
| [Psyche](https://psyche.co/feed.rss) | 20 | Psychology and philosophy |
| [SAPIENS](https://www.sapiens.org/feed/) | 10 | Anthropology and history |
| [The Public Domain Review](https://publicdomainreview.org/rss.xml) | 100 | Historical essays and collections |
| [Ken Shirriff](https://www.righto.com/feeds/posts/default) | 25 | Computing history and reverse engineering |
| [Eli Bendersky](https://eli.thegreenplace.net/feeds/all.atom.xml) | 5 | Software and systems |
| [Lilian Weng](https://lilianweng.github.io/index.xml) | 53 | AI research explanations |
| [Jay Alammar](https://jalammar.github.io/feed.xml) | 10 | Illustrated machine-learning explanations |
| [Distill](https://distill.pub/rss.xml) | 52 | Machine-learning research archive |
| [CDM](https://cdm.link/feed/) | 10 | Music production and creative tools |
| [Valhalla DSP](https://valhalladsp.com/feed/) | 10 | Audio and DSP |
| [Dean Radin](https://deanradin.blogspot.com/feeds/posts/default) | 25 | Parapsychology and research commentary |

Nautilus remains a disabled candidate: direct XML fetching succeeded, but the application's DNS/address checks rejected its resolved address. The source was not enabled and network safety rules were not relaxed. Existing source settings and disabled choices are preserved.

## Finding older material outside the catalog

Six editable `web-archive` sources search historical [Hacker News Search](https://hn.algolia.com/api) submissions:

| Source | Query | Topic |
| --- | --- | --- |
| Older web — AI explanations | `neural networks` | Frontier AI |
| Older web — systems engineering | `distributed systems` | Software engineering |
| Older web — consciousness | `consciousness` | Consciousness & philosophy |
| Older web — archaeology | `archaeology` | Speculative history & mysteries |
| Older web — sound design | `sound synthesis` | Music discovery |
| Older web — psi research | `remote viewing` | Parapsychology |

Each scheduled refresh chooses a historical year/page using the query and UTC day, makes at most two index requests and checks at most three previously unseen original links. An empty year falls back to a bounded older-results sample. Scheduling is at least daily, with a 60-second fetch deadline and existing source health/backoff. Manual tests/refreshes remain available. Already imported canonical URLs are skipped; ordinary upserts still protect deduplication and saved/read/publication choices.

All six queries returned real original links in the live check. Original metadata included a [2012 consciousness essay](https://www.theguardian.com/science/2012/mar/01/consciousness-eight-questions-science) and a [2025 experimental archaeology feature](https://arstechnica.com/science/2025/01/inside-the-hands-on-lab-of-an-experimental-archaeologist/). Some originals supplied no date or refused metadata access. A sound-synthesis link returned 404; that observation led to explicit 404/410 exclusion, covered by a regression test. Robots restrictions and permissions are respected; denied metadata access leaves an honest link-only record, while confirmed missing pages are skipped.

This is discovery through the HN index, not a whole-web search engine. Publisher feeds provide another route into older work. A refresh can import fewer than three items or none; there is no synthetic filler, AI requirement, media downloading or full-article extraction. Public browsing never triggers ingestion.

## Activate on the hosted instance

The source pack remains opt-in. Deploy the tested code through [the hosted release procedure](public-hosting.md), including its pre-upgrade encrypted backup, preservation check, current-release backup/restore rehearsal and interactive administrator launch check. Do not begin maintenance until the operator can complete that password prompt. The original desktop production snapshot remains untouched.

Once the new hosted release is live, run these from the checkout. Each invocation prompts for the administrator password with hidden input; do not put it in arguments or chat:

```powershell
python scripts/apply-source-pack.py --base https://rabbithole.quintonloges.me/api --report data/curation/hosted-source-plan.json
python scripts/apply-source-pack.py --base https://rabbithole.quintonloges.me/api --apply --report data/curation/hosted-source-expansion.json
```

The plan reports missing sources and preserved existing ones. Apply creates missing enabled pack entries as disabled, tests their real response, imports once, then enables them after success. It preserves existing subscriptions and their settings; do not add `--refresh` unless an existing-source refresh is intended. Failures remain disabled with a report. The pack also contains earlier curated choices; the read-only plan makes any older missing choices visible before application. `--only 'Distill'` or another exact source name limits the operation. Individual choices are also available in **Settings → Sources → Browse suggested sources**.

After applying, check source health/jobs, a second refresh for duplicates, public date labels and the daily mix. Disabling an older-web source stops scheduled discovery; hiding an item removes it from public output independently of source enablement. No new environment variable or API key is required for this provider.
