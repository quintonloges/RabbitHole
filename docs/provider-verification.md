# Provider verification (17 October 2026)

## Expanded personal collection

The current live result is **23 active verified sources and 261 retained real items**, superseding the smaller baseline described below. See [feed-curation-report.md](feed-curation-report.md) for every active endpoint/query, last successful retrieval, next due time, linked count, selected individual imports, audit findings, and disabled-source failures. The reusable opt-in configuration is [curated-source-pack.json](../config/curated-source-pack.json).

Live OpenAlex first-page requests succeeded without a key, while cursor continuation returned HTTP 401, consistent with its [current authentication documentation](https://help.openalex.org/api/authentication/). The app now runs an explicitly bounded newest-page sample when no key is present. Title/abstract scoping and the `title:` option follow [official search documentation](https://help.openalex.org/api/searching/). Five focused watches subsequently refreshed successfully with zero new duplicates. Exact original dates remain provider values.

The new focused arXiv watches returned timeouts/429 and remain disabled; the selected Butlin 2023 report returned 502. One landmark arXiv paper imported successfully before those failures. Variety of Sound parsed externally but was rejected by production DNS safety checks. No safety bypass was used. Both real artist SoundCloud and instructional YouTube players loaded in the local reader; no download/transcript access or audible playback is claimed.

## Earlier initial verification

RabbitHole does not automatically subscribe to external sources. The user-selectable feed presets were checked with live `curl.exe -IL --max-time 15` requests on 17 October 2026:

| URL | Live response | Preset |
|---|---|---|
| https://openai.com/news/rss.xml | HTTP 200, `text/xml` | Yes |
| https://www.quantamagazine.org/feed/ | HTTP 200, `application/rss+xml` | Yes |
| https://export.arxiv.org/rss/cs.AI | HTTP 200, `application/rss+xml` | Yes |
| https://www.anthropic.com/news/rss.xml | HTTP 404 | No |

These were header checks, so they verify the endpoints' status and content type at that time, not every entry in their feeds. Feed parsing has deterministic fixture tests. Live availability can change; refresh failures appear on the source and job records and receive bounded exponential backoff.

Additional live header checks: `https://api.openalex.org/works?search=consciousness&per_page=1` returned HTTP 200 with `application/json`; `https://export.arxiv.org/api/query?search_query=all:consciousness&start=0&max_results=1` returned HTTP 200 with `application/atom+xml`. These checks do not prove credentials, quotas, or individual record imports. Keyless OpenAlex access is supported for small requests; configure `OpenAlex__ApiKey` (or `OPENALEX_API_KEY`) for a larger personal budget. The key remains server-side.

YouTube's first-party `https://www.youtube.com/oembed` endpoint returned HTTP 200 and `application/json` for the example video `M7lc1UVf-VE` on 17 October 2026. The response contained `title`, `author_name`, and `thumbnail_url`; no publish date was inferred. URL import uses its title and creator metadata when available, while constructing the embed URL independently from the validated video ID. The oEmbed response's HTML is discarded. A failed oEmbed request leaves an explicit link-only video.

SoundCloud's documented example track `https://soundcloud.com/forss/flickermood` returned HTTP 200 and `application/json` from `https://soundcloud.com/oembed` on 17 October 2026. The response contained `title`, `author_name`, and `thumbnail_url`, but no canonical `url` field, so the importer retains the user supplied track URL. This was a metadata check, not an in-app playback test.

A temporary loopback API using SQLite completed a live Quanta refresh and imported two real feed records. A second refresh exposed a 304 handling bug, which was corrected by handling `Not Modified` before generic redirects. A later Docker app repeat refresh completed with zero imports and nine records retained. The temporary SQLite database was removed.

Official interface references consulted:

- [OpenAlex API](https://help.openalex.org/api/) and [authentication](https://help.openalex.org/api/authentication/) document `/works`, search, singleton records, keyless access, and optional API key use.
- [OpenAlex work attributes](https://help.openalex.org/data/works/attributes/) define `abstract_inverted_index` as source-provided word positions, `best_oa_location` and `open_access.oa_url` as open-access locations, `cited_by_count` as an incoming citation count, and `referenced_works` as outgoing OpenAlex work IDs. RabbitHole reconstructs only complete, bounded abstract indexes and labels the result `provider-abstract`; missing or malformed indexes leave the excerpt and body empty. Paper metadata uses `openAccessUrl`, `openAccessStatus`, `citedByCount`, `referencedWorks` (validated OpenAlex URLs, first 100), `referencedWorksCount`, and `referencedWorksTruncated`. The app does not fetch OA PDFs or referenced works automatically.
- [arXiv API access](https://info.arxiv.org/help/api/index.html) describes public API access and asks applications to acknowledge arXiv data usage. RabbitHole uses the Atom search API and retains original links.
- [SoundCloud oEmbed](https://developers.soundcloud.com/docs/oembed) documents `https://soundcloud.com/oembed` for user-provided SoundCloud URLs. RabbitHole constructs an allowlisted widget URL and does not execute the returned HTML.
- [YouTube player reference](https://developers.google.com/youtube/iframe_api_reference) documents supported embeds. RabbitHole uses channel feeds or validated video IDs and does not fetch captions or media.

Provider limits: arXiv search results and feeds are metadata and excerpts, not licensed full text. SoundCloud import requires a track URL; no uncredentialed global track search is implied. YouTube channel feeds require the canonical `/channel/UC…` ID form; arbitrary handles need a user-supplied video URL or channel ID. Manual website metadata is fetched only when robots rules allow it; blocked or failed extraction produces an explicit link-only item. No external HTML is stored as executable content.

## Running Docker app smoke (17 October 2026)

Through `http://localhost:3000/api`, a fresh PostgreSQL catalog was confirmed empty. Three real sources were added with `resultLimit=3`: Quanta Magazine (RSS, enabled at a 24-hour interval), OpenAlex `consciousness philosophy of mind` (disabled after manual refresh), and arXiv `consciousness` (disabled after manual refresh). Quanta's scheduled refresh imported 3; its manual repeat imported 0. OpenAlex and arXiv each imported 3, then each repeat imported 0. `GET /api/items?limit=30` reported 9 total items. After an API rebuild, another Quanta refresh imported 0 and kept total at 9.

| Provider | Actual imported title and original link | Raw HTTP JSON `publishedAt` |
|---|---|---|
| Quanta RSS | [Triplet of ‘Ghostly’ Galaxies Hints at What Dark Matter Is — and Isn’t](https://www.quantamagazine.org/triplet-of-ghostly-galaxies-hints-at-what-dark-matter-is-and-isnt-20261014/) | `2026-10-14T14:04:05.0000000+00:00` |
| Quanta RSS | [How Do Fungi Keep the Planet Healthy?](https://www.quantamagazine.org/how-do-fungi-keep-the-planet-healthy-20261015/) | `2026-10-15T13:40:13.0000000+00:00` |
| Quanta RSS | [He Wants AI Benchmarking To Be a Science, Not a Dark Art](https://www.quantamagazine.org/he-wants-ai-benchmarking-to-be-a-science-not-a-dark-art-20261016/) | `2026-10-16T13:25:08.0000000+00:00` |
| OpenAlex | [Consciousness in Spinoza's Philosophy of Mind](https://doi.org/10.1111/j.2041-6962.2007.tb00052.x) (`W2056981297`) | `2007-06-01T00:00:00.0000000+00:00` |
| OpenAlex | [Matter and Consciousness: A Contemporary Introduction to the Philosophy of Mind](https://doi.org/10.1111/j.1468-0149.1986.tb01163.x) (`W1555373269`) | `1986-04-01T00:00:00.0000000+00:00` |
| OpenAlex | [Matter and consciousness: A contemporary introduction to the philosophy of mind](<https://doi.org/10.1016/0376-6357(85)90028-2>) (`W278287432`) | `1985-08-01T00:00:00.0000000+00:00` |
| arXiv | [How Could AI Eliminate Humanity? A Failure-Mode Analysis of Civilizational Risk](https://arxiv.org/abs/2610.08878v1) (`2610.08878`) | `2026-10-06T07:28:29.0000000+00:00` |
| arXiv | [Some Twitch chats exhibit multifractal characteristics resembling stream-of-consciousness narrative](https://arxiv.org/abs/2610.10654v1) (`2610.10654`) | `2026-10-07T16:23:08.0000000+00:00` |
| arXiv | [Awakening as a Trait-Level Transformation of Consciousness](https://arxiv.org/abs/2610.20856v1) (`2610.20856`) | `2026-10-14T19:46:41.0000000+00:00` |

The table values were read from `Invoke-WebRequest.Content` and parsed with `ConvertFrom-Json -DateKind String`, preserving the actual wire strings. Earlier `Invoke-RestMethod` checks converted dates into the PowerShell process's local time zone and misleadingly showed `-04:00` or `-05:00` values. OpenAlex's source records report date-only publication values `2007-06-01`, `1986-04-01`, and `1985-08-01` for those three works. The API wire values are midnight UTC on the correct dates, and the frontend now displays publication dates in UTC; a Playwright date regression passed in the seven-test end-to-end run. The importer also retains the exact source date in `metadata.sourcePublishedDate` in the final API build. A final live refresh completed with zero new records, retained nine catalog items, and populated those three exact source dates. Quanta list entries intentionally omit `body`; `GET /api/items/{id}` returned 396 characters of feed-supplied text for the first Quanta item with `textProvenance=feed-content`.
