# Ingestion, privacy and limits

RabbitHole stores one person's collection locally. There are no analytics, accounts, advertisements or remote database services. Internet egress comes from configured source refreshes, explicitly requested research/URL lookups, opted-in media players and explicitly invoked AI actions. Fonts and visual assets do not require a third-party CDN.

## Source handling

- RSS/Atom supplies actual titles, authors, dates, excerpts and full feed text when present. HTML is reduced to plain text. A missing publication date stays unknown. Malformed feeds fail independently.
- OpenAlex and arXiv search/watch results retain paper metadata and abstracts when available, not invented full text. API budgets/authentication can change; inspect actual health rather than assume availability. RabbitHole acknowledges arXiv for use of its open-access interoperability API.
- YouTube uses canonical video IDs, official oEmbed metadata and public channel feeds. Video playback is through a supported player, loaded only when requested. Arbitrary captions/transcripts are not fetched. The official captions download API requires owner/editor permissions.
- SoundCloud uses official oEmbed metadata and an allowlisted widget. Track/playlist import does not promise global track discovery or downloads. Artist/music topic preferences are editable taste terms, not fabricated playable records.
- Manual websites are metadata/excerpt imports, with robots checks and honest link-only fallback. Full article bodies are not scraped to evade licensing, paywalls or access controls.
- Local PDF/EPUB originals stay on the private upload volume. EPUB chapter order comes from the real spine. TOC labels are matched to spine documents; several fragment-only TOC entries in one XHTML file remain one section. PDF pages are always retained; real bookmarks and user page ranges supplement them. Complex layout may extract imperfectly. OCR, DRM removal and EPUB embedded media execution are not implemented.

## URL and parser boundaries

The general fetcher rejects non-HTTP(S), credentials in URLs, unexpected ports, private/reserved addresses and local hostnames. DNS is checked and the socket connects to a checked IP; every redirect is revalidated. Redirects, response length, MIME and request duration are bounded. HTTP errors and retry timing are recorded without provider credentials or response bodies in logs. It does not use arbitrary proxy environment variables to bypass checks.

External HTML/Markdown is never executed. Original links use safe schemes and new-tab isolation. Provider iframe origins are allowlisted and playback requires a user click. Library originals are served with `nosniff` and a sandbox policy. Browser PDF rendering is the browser's built-in viewer; EPUB files download for an external reader when original viewing is selected.

Uploads are limited to 50 MiB. EPUB limits cover entry count, declared expanded size, compression ratio, unsafe names, XML entities and encrypted packages. PDFs are limited to 2,000 pages, extracted text/output size and an isolated worker deadline/heap. Parsing failure preserves an original-file fallback. The container also has a memory limit. This is defensive processing of personal files, not a public multi-user upload service.

## Secrets, backup and AI

Environment values are not exposed in the frontend, logs or export. Status endpoints expose only whether optional providers are configured. Keep `.env`, uploads and backups out of version control. A backup/export contains personal notes and potentially private copyrighted book text; it is for personal recovery, not publication.

AI sends only explicitly selected bounded passages and a user response to the configured endpoint. Set a trusted local endpoint and `AI_ALLOW_LOCAL=true` to use a local compatible service. Local AI networking is intentionally separate from arbitrary public-web fetching. No source text can cause the model to execute application actions. Model prose is still fallible; structural validation and actual references are not a guarantee of factual correctness.

## Intentional product boundaries

Unconventional interests are not assigned credibility scores or corrective labels. Personal topic/source controls determine discovery. Counterarguments are a requested exploration action, never an automatic intervention. Transient curiosity does not mutate topic weights. The finite edition is a default pace; more items remain accessible on demand.

Optional Semantic Scholar enrichment, authenticated YouTube search, global SoundCloud search, OCR, automatic transcript ingestion and automatic JSON archive merge-restore are not implemented. Their working alternatives are arXiv/OpenAlex, channel feeds/known URLs, official embeds, original-file viewing, source text and database/files backup restore. There is no mandatory external account or AI provider.
