# Architecture

RabbitHole is one local application with four containers: Caddy, Next.js, ASP.NET Core and PostgreSQL. Caddy is the only service with a published host port (`127.0.0.1:3000`). `/api/*` and `/openapi/*` go to the API; all other paths go to Next. The API applies EF migrations before accepting traffic. Database and original documents use separate persistent volumes.

## Data and provenance

Content records retain canonical URL, provider IDs, author, nullable source publication time, ingestion time, excerpt and permitted plain text. Local chapters point to documents and sections. Source text never shares a field with AI synthesis. URL/DOI/arXiv/provider uniqueness plus serialized upserts prevent repeat refreshes from erasing state. Provider provenance accumulates when an identifier merges records from different services.

User state, notes/highlights, branch nodes, editions and practice attempts are separate rows with foreign keys. Opening records `lastOpenedAt`; read and understood are explicit booleans. Practice responses and self-ratings are evidence of an attempt, not independently verified recall or a mastery score. Topic keywords/preferences are editable; curiosity-only topics are distinct from lasting interests.

Rabbit holes are persistent trees. A node references a real item or a user question; its parent must belong to the same hole. Siblings are retained, duplicate item visits are allowed as distinct nodes, and navigation updates the hole's last-node pointer. Related recommendations use shared topics and lexical overlap, with actual source IDs for inspection. Free questions are never represented as retrieved publications.

## Ingestion

The API's background service polls enabled due sources with per-source serialization, bounded response sizes, timeouts, conditional requests and exponential backoff. Persistent source health and jobs expose success/failure. Adapters normalize RSS/Atom, OpenAlex, arXiv and YouTube channel records. The external HTTP boundary is injectable for deterministic tests.

Arbitrary user URLs pass public-host, DNS, port, redirect and connect-time IP checks. Metadata-only web import consults robots rules and falls back to a link-only record. Provider embeds are constructed from validated IDs/URLs; returned oEmbed HTML is never injected into the app. No transcript scraping, media downloading or paywall bypassing is involved.

Document uploads are bounded, signature checked and hashed. Parsing runs in a child .NET process with a deadline, GC heap limit, bounded output and one upload at a time. Zip entries are read in memory without extraction, with entry/expansion/path/DTD/DRM checks. PDF extraction uses PdfPig; actual pages and available bookmarks become sections. EPUB sections follow manifest/spine order and actual TOC labels. The original remains available on extraction failure. User-defined PDF ranges add records without deleting page notes/progress.

## Discovery and learning

Daily editions persist ordered item IDs and rationale. Page reloads reuse that selection. A user-requested new selection or more-items action changes it deliberately. Ranking uses explicit topic weights/pins, bounded continuity from read material and saved trails, feedback, freshness and diversity, with space for candidates outside core interests. Neither AI nor embeddings are required. This is a deterministic heuristic, not a psychological model.

PostgreSQL GIN indexes support title/excerpt/body search and note search. Partial lexical matching handles short terms; SQLite integration tests use a lexical fallback. Related items and short reading sequences use records already present in the catalog.

Reflection prompts reference selected records and available source passages. Again/Hard/Good/Easy adjust a simple review interval/ease value; Not now moves the due time without a review credit. Optional AI critique is separate from this self-report. There are no execution sandboxes for user code because engineering practice is text reasoning only.

## Optional AI

The server reads endpoint/model/key from private environment configuration. AI is off until enabled in Settings, and no background enrichment runs. An explicit action sends at most two 6,000-character source passages plus a bounded user response. One request runs at a time, with six actions/minute, bounded tokens, deadline, one conservative retry and a content/model-specific cache.

Responses must parse as structured JSON and reference supplied IDs. Unknown citations, unsupported URLs and invalid quoted passages are rejected. Source data is framed as untrusted evidence. The provider has no tools and cannot fetch/import/delete anything. Structural checks cannot establish the truth of generated prose; the UI labels it synthesis and retains original sources for comparison.

## Validation boundaries

xUnit tests exercise the real relational services with SQLite and fixture HTTP. The E2E host runs the actual API over Kestrel and the actual Next UI. It is a separate project, never compiled into the production application. Docker smoke checks exercise PostgreSQL migrations, full-text search, real provider imports and persistence separately. The acceptance report records what actually ran.
