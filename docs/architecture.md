# Architecture

RabbitHole is one local application with four containers: Caddy, Next.js, ASP.NET Core and PostgreSQL. Caddy is the only service with a published host port (`127.0.0.1:3000`). `/api/*` and `/openapi/*` go to the API; all other paths go to Next. The API applies EF migrations before accepting traffic. Database and original documents use separate persistent volumes.

## Data and provenance

Content records retain canonical URL, provider IDs, author, nullable source publication time, ingestion time, excerpt and permitted plain text. Local chapters point to documents and sections. Source text never shares a field with AI synthesis. `ItemIdentities` owns unique, hashed identity keys for URLs, normalized DOIs, arXiv IDs and provider IDs. RSS GUIDs are scoped to a subscription's identity version; changing its feed URL/kind/query starts a new version. Display fields on `ContentItem` are snapshots, not independent uniqueness constraints.

An item has multiple `ItemSources` origins. Disabling one subscription does not suppress an item supplied by another enabled source. Removing or replacing a subscription retains its origin snapshot. Source filters include these historical origins. A bridge between existing records merges them in one transaction: notes, branches, prompts and AI records keep their IDs and point to the oldest surviving item; saved/read/understood flags are combined, progress takes the maximum, and the less restrictive dismissal state wins. Alias records and their original state remain available in exports. Old item links resolve to the survivor; historical edition/evidence references remain intact and editions collapse duplicate aliases on display.

`ItemTopics` separates source assignments, lexical keyword matches, legacy tags with unknown provenance, and explicit user inclusions/exclusions. `ContentItem.TopicIds` is the effective cached projection used by ranking and the UI. User overrides survive refreshes, document retries and keyword edits. The same classifier covers external records and local chapters, without AI. Changing topic keywords or source tags reclassifies the existing catalog.

PostgreSQL forward migrations add the catalog tables and source watch/version fields. Startup then performs an idempotent transactional backfill before serving traffic. The SQLite development path has an additive schema upgrade with equivalent backfill. Existing item, note, state, document, section, trail and practice IDs survive. Previously discarded source associations or overwritten text cannot be reconstructed from the old database; known provider references are recovered where present. Downgrades require restoring a matching pre-upgrade database/uploads backup, because the old schema cannot represent retained aliases and origin versions safely.

User state, notes/highlights, branch nodes, editions and practice attempts are separate rows with foreign keys. Opening records `lastOpenedAt`; read and understood are explicit booleans. Practice responses and self-ratings are evidence of an attempt, not independently verified recall or a mastery score. Topic keywords/preferences are editable; curiosity-only topics are distinct from lasting interests.

Rabbit holes are persistent trees. A node references a real item or a user question; its parent must belong to the same hole. Siblings are retained, duplicate item visits are allowed as distinct nodes, and navigation updates the hole's last-node pointer. Related recommendations use shared topics and lexical overlap, with actual source IDs for inspection. Free questions are never represented as retrieved publications.

## Ingestion

The API's background service polls enabled due sources with per-source serialization, bounded response sizes, timeouts, conditional requests and exponential backoff. Source edits/deletion share that serialization, preventing a stale refresh from restoring an old query's cursor. Each provider has a separate database context. Upserts are transactional and discard failed tracked changes before persisting retryable health. Adapters normalize RSS/Atom, OpenAlex, arXiv and YouTube channel records. The external HTTP boundary is injectable for deterministic tests.

Interactive OpenAlex searches retain relevance ordering. Watches use native title/abstract scope; an explicit `title:` prefix selects title-only matching. Initial sampling covers the last two years, bounded above by the poll date. Later windows overlap seven days. With an API key, each refresh consumes one bounded cursor page, storing its fixed window and continuation for restart; the checkpoint advances only after the window finishes. Pending pages resume after one minute and failed pages leave progress unchanged. Without a key, only a bounded first-page sample is fetched on the normal schedule, because further cursor pages require authentication. Settings explains that this mode can miss papers; it does not claim exhaustive coverage. Indexing delays longer than the overlap can require an explicit search. arXiv watches preserve documented field and Boolean expressions instead of adding `all:` to structured queries.

RSS text normalization drops script/style/comment content and keeps named Atom authors without concatenating email/URI fields. Manual metadata recognizes author and publication meta tags, never modification or retrieval time as publication. An optional `feedUrl` on URL import retrieves a bounded real feed through the existing safe fetcher and imports only the entry matching the requested canonical URL. It preserves feed provenance without creating a subscription or importing unrelated entries.

Arbitrary user URLs pass public-host, DNS, port, redirect and connect-time IP checks. Metadata-only web import consults robots rules and falls back to a link-only record. Provider embeds are constructed from validated IDs/URLs; returned oEmbed HTML is never injected into the app. No transcript scraping, media downloading or paywall bypassing is involved.

Document uploads are bounded, signature checked and hashed. Parsing runs in a child .NET process with a deadline, GC heap limit, bounded output and one upload at a time. Zip entries are read in memory without extraction, with entry/expansion/path/DTD/DRM checks. PDF extraction uses PdfPig; actual pages and available bookmarks become sections. EPUB sections follow manifest/spine order and actual TOC labels. The original remains available on extraction failure. User-defined PDF ranges add records without deleting page notes/progress.

Interrupted/failed/original-only documents expose Retry extraction. The retry endpoint and same-hash reupload reuse the document and reconcile sections by locator, preserving IDs, notes, progress, edited metadata and manual ranges. Extraction failure keeps prior text. A missing original is reported explicitly; uploading the same file restores it. Retry and upload share the same import gate.

## Discovery and learning

Daily editions persist ordered item IDs and rationale. Page reloads reuse that selection. A user-requested new selection or more-items action changes it deliberately. Ranking uses explicit topic weights/pins, bounded continuity from read material and saved trails, feedback, freshness and diversity, with space for candidates outside core interests. Neither AI nor embeddings are required. This is a deterministic heuristic, not a psychological model.

The curation pass adds a soft per-source daily cap (15%, minimum two, while alternatives exist), near-duplicate title penalties, and a small deterministic rotating boost for items older than 180 days. Unknown publication dates receive no freshness bonus. Related results use substantive word rarity, shared title phrases, existing permitted body text when a teaser is sparse, and source diversity. Dismissed and disabled-only records are excluded; broad-topic alternatives are labeled as such and each recommendation cites the starting record. This remains lexical navigation, with no credibility score or claim of semantic certainty.

Human-editable source/reading packs under `config/` are opt-in tooling, not startup seed data. The Python installers use the ordinary API, match source identities or item identifiers, preserve existing preferences, and leave unverified new sources disabled. The audited live outcome is in [feed-curation-report.md](feed-curation-report.md).

PostgreSQL GIN indexes support title/excerpt/body search and note search. Partial lexical matching handles short terms; SQLite integration tests use a lexical fallback. Related items and short reading sequences use records already present in the catalog.

Reflection prompts reference selected records and available source passages. Again/Hard/Good/Easy adjust a simple review interval/ease value; Not now moves the due time without a review credit. Optional AI critique is separate from this self-report. There are no execution sandboxes for user code because engineering practice is text reasoning only.

Practice drafts and asynchronous critique results belong to individual prompts; snoozing another prompt cannot submit or erase the active draft. Item actions notify parent caches only after persistence succeeds. Local optimistic controls roll back on failure.

## Optional AI

The server reads endpoint/model/key from private environment configuration. AI is off until enabled in Settings, and no background enrichment runs. An explicit action sends at most two 6,000-character source passages plus a bounded user response. One request runs at a time, with six actions/minute, bounded tokens, deadline, one conservative retry and a content/model-specific cache.

Responses must parse as structured JSON and reference supplied IDs. Unknown citations, unsupported URLs and invalid quoted passages are rejected. Source data is framed as untrusted evidence. The provider has no tools and cannot fetch/import/delete anything. Structural checks cannot establish the truth of generated prose; the UI labels it synthesis and retains original sources for comparison.

## Validation boundaries

xUnit tests exercise the real relational services with SQLite and fixture HTTP. The E2E host runs the actual API over Kestrel and the actual Next UI. It is a separate project, never compiled into the production application. Docker smoke checks exercise PostgreSQL migrations, full-text search, real provider imports and persistence separately. The acceptance report records what actually ran.
