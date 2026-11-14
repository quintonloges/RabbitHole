# Source expansion and older web discovery

## Morning Dew and its AI sources — 14 November 2026

Reviewed the **AI** sections in the ten [Morning Dew](https://www.alvinashcraft.com/feeds/feed.xml) editions dated 2–13 November 2026: 231 outbound links. The local source pack now includes Alvin Ashcraft's roundup and seven publishers it links to. Each upstream source records a concrete Morning Dew issue and original article in `discoveredVia`; the full roundup remains attributed to Alvin. This is a reviewed selection of subscriptions, not automatic following of every outbound link.

| Source | Verified feed entries | Coverage |
| --- | ---: | --- |
| [Morning Dew — Alvin Ashcraft](https://www.alvinashcraft.com/feeds/feed.xml) | 20 | Complete daily developer roundups, including the AI section |
| [O'Reilly Radar](https://www.oreilly.com/radar/feed/) | 15 | AI engineering, agents, evaluation and software work, plus broader technology |
| [JetBrains AI](https://blog.jetbrains.com/ai/feed/) | 12 | AI-specific coding tools, code retrieval, field notes and product updates |
| [Bart Wullems](https://bartwullems.blogspot.com/feeds/posts/default) | 25 | Hands-on agents, Copilot and MCP alongside .NET, cloud and architecture |
| [Model Context Protocol Blog](https://blog.modelcontextprotocol.io/index.xml) | 29 | Protocol maintainers' implementation notes, releases and interoperability |
| [Microsoft Foundry Blog](https://devblogs.microsoft.com/foundry/feed/) | 10 | Agents, model evaluation, local models and Azure tooling |
| [The New Stack](https://thenewstack.io/blog/feed/) | 26 | AI development reporting and practitioner articles, plus cloud/infrastructure |
| [InfoQ — AI, ML & Data Engineering](https://feed.infoq.com/ai-ml-data-eng/) | 15 | Topic-specific architecture, production AI and data engineering |

All eight passed RabbitHole's actual safe fetcher and RSS/Atom parser within the existing 1.5 MB feed limit. The feeds supplied publication dates; missing authors in the MCP feed remain unknown. InfoQ supplies metadata/excerpts, and original articles remain on its website. Morning Dew imports one roundup per item; its embedded AI links do not become separate catalog items automatically. Broader feeds are not silently filtered to AI. Simon Willison, Sean Goedecke, Hugging Face and Martin Fowler already exist in the pack and were not duplicated.

The new choices refresh daily, with recent-result limits of 3 for Morning Dew, 5 for Radar/JetBrains/Bart/MCP/Foundry, and 8 for The New Stack/InfoQ. Enabled subscriptions also use daily RSS archive discovery where publisher feeds/archives permit it. This adds no new adapter or credential requirement and promises neither complete history nor every article Alvin selects. Jim Bennett was also linked repeatedly, but his advertised feed exceeded the research fetcher's 2 MB ceiling, already above the app's 1.5 MB limit; it was not added as a working subscription and limits were not raised.

**Activation status:** all eight were explicitly enabled in local development (`127.0.0.1:47832`) on 14 November 2026, with a 1,440-minute refresh interval and daily backfill of up to three unseen items each. Initial refreshes added **44 items**, and the first archive batches added **24**, for **68 new items**. Authenticated verification after rebuilding development confirmed all eight enabled, healthy and scheduled for the following day. Hosted and personal production remain unchanged.

Activation exposed two RSS edge cases for InfoQ: its feed host returns 406 for `robots.txt`, and its supplied article links use a separate host. The adapter now accepts unavailable robots representations and imports validated supplied metadata without crawling the article host. A live in-memory check drained seven remaining feed entries in batches of 3, 3 and 1; no usable deeper archive was exposed by the checked feed host. After the local fix, InfoQ's failed first batch was retried successfully and its daily schedule resumed. Archive coverage remains limited to what each publisher exposes.

## AI creators and interviews — 11 November 2026

The editable source pack now includes the owner's requested AI interests:

| Source | Subscription | Coverage |
| --- | --- | --- |
| [Sean Goedecke](https://www.seangoedecke.com/) | [Advertised RSS](https://www.seangoedecke.com/rss.xml), daily, up to 10 entries | Practitioner essays on AI, agents and software engineering |
| [Dr. Josh C. Simmons](https://www.youtube.com/@drjoshcsimmons) | Verified channel `UC4sdJIrKWKxymZ5lwVSBzTw`, daily, up to 15 videos | Hands-on model experiments, plus cybersecurity videos, livestreams and Shorts |
| [Dwarkesh Podcast](https://www.dwarkesh.com/) | [Publisher RSS](https://www.dwarkesh.com/feed), daily, up to 10 entries | Researcher interviews alongside essays, history and other subjects |

These are editable source choices under the existing Frontier AI, Software engineering and Unexpected finds topics. They do not hard-code personal ranking or turn every adjacent topic into a new lasting interest. The source picker now includes the supported `youtube` kind, preserving video identity and original-player behavior.

The reading pack separately selects five original videos: the requested [Qwen refusal-direction experiment](https://www.youtube.com/watch?v=eT9G5cO-nY8), the same creator's [Qwen neuron-steering experiment](https://www.youtube.com/watch?v=Uvtg6AezSwo), and Dwarkesh interviews with [Ilya Sutskever](https://www.dwarkesh.com/p/ilya-sutskever-2), [Dario Amodei](https://www.dwarkesh.com/p/dario-amodei-2) and [Demis Hassabis](https://www.dwarkesh.com/p/demis-hassabis). The publisher's interview pages link the selected videos. This preserves useful older selections outside finite recent feeds; it is not a promise to search all of YouTube for similar videos.

All three feeds passed the app's safe fetcher and parser with its existing response-size limits: Sean 30 entries, Josh 15, Dwarkesh 20. All five videos returned their real titles and creators through official YouTube oEmbed. Creator titles remain attributed source text. No captions or media are downloaded, and oEmbed's missing publication date remains unknown. Sean's RSS supplies original text and dates but omits per-entry author fields. Channel uploads and Dwarkesh entries also cover subjects beyond AI; there is no hidden guest/topic filter.

**Activation status:** the owner chose to keep these additions local on 11 November 2026. They are available in the rebuilt development source picker and editable packs; subscriptions and selected imports have not been applied to the hosted or personal production catalog. Source and reading installers still require an explicit apply and normal administrator authentication. Existing subscriptions, visibility choices and personal state are preserved.

## Gradual daily backfill

Daily archive discovery applies to all compatible enabled subscriptions, including existing sources, rather than a fixed list of creators. The additive upgrade defaults to **3 previously unseen items per source per day**. In **Settings → Sources**, choose 1–10, turn off **Discover older content daily**, inspect separate archive status/last/next checks, or request **Find older now**. A manual check may run an extra batch before the next scheduled day. Disabling a source or pausing ingestion stops both kinds of discovery. Cursors survive restart; edits to source identity or archive URL restart archive discovery without removing imported items. Known records, publication choices and saved/read state are preserved.

| Source | Older-content path | Limitations |
| --- | --- | --- |
| RSS/Atom, including podcast feeds | Unused entries beyond the recent refresh limit, then advertised feed pagination, publisher sitemaps or linked HTML archive pages | Crawled archives stay within publisher scope with robots permissions and bounded requests; supplied feed metadata may link to another public host without fetching it. Optional same-host archive URL. Missing archives are shown explicitly and revisited after a week; remaining feed entries are still checked daily. |
| YouTube channel | yt-dlp reads public uploads into small persisted metadata batches, including archived uploads outside the recent feed | No API key required. Docker bundles the runtime. Only metadata and original links; no captions/media downloads or account cookies. |
| OpenAlex query | Separate cursor through matching papers older than 30 days | Requires `OPENALEX_API_KEY` for archive pagination; ordinary keyless newest-page refresh remains available. |
| arXiv query | Submitted-date pages through matching papers older than 30 days | Public API, shared pacing, bounded pages; broad queries may encounter provider result ceilings. |
| Older web (`web-archive`) | Existing daily historical search | Already backfills; no redundant second job. |

Archive traversal need not be chronological. It can add fewer than the daily limit or no items, depending on previously imported records, available metadata, provider permissions and rate limits. Exhausted provider snapshots are revisited so later indexing can still expose older work. RSS pages retain only metadata/excerpts unless the feed itself supplies original text. Publication dates come from originals: sitemap modification times and YouTube playlist-added times never become publication dates. Unknown dates remain unknown.

RSS archive traversal reserves space for nested sitemap children within its 32-page queue and three-page request budget. Previously full queues from the earlier traversal format restart from advertised roots once, skipping already imported records and preserving personal state. Same-publisher HTTP-to-HTTPS upgrades are accepted for feeds, robots and archives; host, port, path, robots and safe-fetcher restrictions still apply, and HTTPS response downgrades are rejected.

New-content refresh and archive backfill have independent cursors, health and scheduling. Archive errors back off from one to seven days, retain their last committed position and do not mark a healthy recent feed as failed. Keys stay in server configuration; cursor tokens are not returned by source endpoints or exports. No newly configured subscriptions are activated by this feature. The requested creator packs remain local pending an explicit apply/deployment.

Interfaces checked against official [YouTube channel uploads metadata](https://developers.google.com/youtube/data/reference/rest/v3/channels), [playlist pagination](https://developers.google.com/youtube/data/reference/rest/v3/playlistItems/list), [OpenAlex authentication](https://help.openalex.org/api/authentication/), [arXiv API](https://info.arxiv.org/help/api/user-manual.html) and [sitemap semantics](https://www.sitemaps.org/protocol.html).

### Keyless YouTube runtime

YouTube archive discovery defaults to the pinned [yt-dlp metadata extractor](https://github.com/yt-dlp/yt-dlp#embedding-yt-dlp), currently **2026.10.26**, installed with the official wheel's SHA-256 hash. Docker builds include an isolated Python environment automatically. For host .NET development on Windows:

```powershell
python -m venv .cache/yt-dlp-runtime
& ./.cache/yt-dlp-runtime/Scripts/python.exe -m pip install --require-hashes --no-deps --only-binary=:all: -r apps/api/Ingestion/yt-dlp-requirements.txt
$env:YTDLP_PYTHON = (Resolve-Path .cache/yt-dlp-runtime/Scripts/python.exe).Path
dotnet run --project apps/api
```

On Linux/macOS use `python3 -m venv` and `.cache/yt-dlp-runtime/bin/python`. No API key is needed. `YOUTUBE_BACKFILL_MODE=api` retains the optional official API adapter and requires `YOUTUBE_API_KEY`; adding a key alone does not change the default mode. Cursor formats are separate, so a mode change restarts discovery while skipping existing identities.

**Check YouTube** uses the configured backfill adapter and persists the result without importing or moving archive progress. In API mode it checks the uploads playlist at the saved page token; missing keys, rejected credentials and invalid page tokens remain failures, without invoking yt-dlp. In the default yt-dlp mode, daily backfills verify live access even while draining a cached batch. Failures remain visible after reload/restart, alongside last success, next retry and recent-source health. Missing Python/helper/dependency, wrong dependency version, network/rate-limit/blocking responses, invalid metadata, request/output budgets and timeouts have explicit messages. A successful diagnostic clears the failure and resumes a backed-off source. Failed attempts leave the cursor and existing library intact. Interrupted running jobs become visible retryable failures.

The extractor is isolated from shell commands, user configuration, plugins, proxies, browser cookies and application secrets. It accepts only validated channel IDs and constructs the public uploads target itself. It does not request media, captions, approximate publication dates, JavaScript challenges or authentication bypasses. Missing exact publication dates stay unknown. Public provider changes can still break extraction: inspect the health message, retry for transient errors, and update the pinned version/hash/helper together when a new release is needed. Rebuild local development with `./scripts/dev.ps1 start`; this does not deploy production.

Large-channel limitation: yt-dlp traverses upload pages to reach the requested position. This release limits a scan to 32 outbound requests, 8 MB per response, 24 MB total and 75 seconds. Very deep archives can hit that bound before their end. A deterministic request/size limit produces a prominent **Archive scan limit reached** failure and suspends automatic archive retries at that position; it does not loop indefinitely or claim the whole archive is imported. **Check YouTube** tests the saved position so a shallow successful request cannot conceal this limit. Manual retry remains available, and recent feeds continue. Full provider continuation across helper processes is not implemented.

## Previously activated expansion — 8 November 2026

On 8 November 2026, production enabled all 15 additional publisher feeds and all six older-web searches. Each search refreshes daily. Their first import added 16 original links.

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

For a new installation or future additions to the pack, select **Verify and activate missing curated sources** when manually queuing the hosted workflow. After the owner's GitHub production approval, the workflow verifies backup restoration, migration, privacy and public HTTPS access, then applies the pack. Existing subscriptions continue on their own schedules across releases. See [the hosted release procedure](public-hosting.md).

Alternatively, once the new hosted release is live, run these from the checkout. Each invocation prompts for the administrator password with hidden input; do not put it in arguments or chat:

```powershell
python scripts/apply-source-pack.py --base https://rabbithole.quintonloges.me/api --report data/curation/hosted-source-plan.json
python scripts/apply-source-pack.py --base https://rabbithole.quintonloges.me/api --apply --report data/curation/hosted-source-expansion.json
```

The plan lists missing and existing subscriptions. Apply creates each new subscription disabled, tests its response, imports once and then enables it. If the activation request times out, the installer checks the saved subscription by ID before reporting success. An unconfirmed activation or a failed test or import produces an error in the report. Existing subscriptions retain their settings. Pass `--refresh` to refresh them explicitly or `--only 'Distill'` to limit the operation to one named source. Individual choices are also available in **Settings → Sources → Browse suggested sources**.

After applying, check source health/jobs, a second refresh for duplicates, public date labels and the daily mix. Disabling an older-web source stops scheduled discovery; hiding an item removes it from public output independently of source enablement. No new environment variable or API key is required for this provider.
