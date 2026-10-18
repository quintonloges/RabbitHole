# Feed curation report

Audited 17 October 2026 (America/New_York); final database snapshot **2026-10-18 03:03:26 UTC**. This report describes the existing local PostgreSQL instance at `http://localhost:3000`, not a demo fixture.

**261 real catalog records; 23 active, successfully retrieved sources; 221 currently eligible for Discover.** The remaining records are retained from disabled watches or explicitly passed-on items. The original nine records are field-for-field identical in the export comparison; existing reading state, eight topics, settings, and original three source preferences are unchanged. Two additive curated trails contain ten real-item nodes.

## What changed

- Activated 17 new RSS/Atom feeds and five focused OpenAlex watches alongside the existing Quanta feed. Daily, two-day, and weekly schedules are editable in Settings.
- Imported 27 deliberately selected classics, original articles, historical references, videos, and artist tracks. OpenAI entries were selected individually from its real RSS; Anthropic and historical references use individual URL imports.
- Retired six broad new research watches after actual relevance failures. Two new arXiv watches, one blocked feed, and one empty psi query remain disabled. No existing subscriptions were removed or changed.
- Passed on four newly imported low-fit records: salon software, FinTech information systems, an investor-revenue story, and journal announcements. Records remain searchable. No credibility labels or ideological scores were added.
- Added narrow ranking/normalization regressions: source diversity, near-duplicate title penalties, controlled older-item rotation, honest unknown dates, substantive terms/phrases for related items, CSS/script removal, Atom creator names, and actual HTML publication/author metadata.

## Source catalog

Each row below is enabled and has a successful production refresh, not merely a successful HTTP status. Counts are unique records linked to that subscription; multi-source records can occur in more than one row. Last/next times are UTC. RSS limits bound each refresh, so this is a curated recent selection rather than exhaustive archival coverage.

| Source / category | Verified endpoint or exact query | Method · limit · interval | Linked records | Last success UTC | Next due UTC | Limitation |
|---|---|---|---:|---|---|---|
| .NET Blog — Software engineering | [RSS/Atom](<https://devblogs.microsoft.com/dotnet/feed/>) | rss · 10 · 24 h | 10 | 2026-10-18 02:50:34 | 2026-10-19 02:50:34 | Feed-supplied text only; link to publisher for remaining content.  |
| Aeon — Unexpected finds | [RSS/Atom](<https://aeon.co/feed.rss>) | rss · 12 · 24 h | 12 | 2026-10-18 02:50:35 | 2026-10-19 02:50:35 | Feed-supplied text only; link to publisher for remaining content.  |
| Attack Technique — Music discovery | [RSS/Atom](<https://www.attackmagazine.com/technique/feed/>) | rss · 10 · 48 h | 10 | 2026-10-18 02:50:35 | 2026-10-20 02:50:35 | Feed-supplied text only; link to publisher for remaining content.  |
| Berkeley AI Research — Frontier AI | [RSS/Atom](<https://bair.berkeley.edu/blog/feed.xml>) | rss · 10 · 48 h | 10 | 2026-10-18 02:51:25 | 2026-10-20 02:51:25 | Feed-supplied text only; link to publisher for remaining content.  |
| EarLevel Engineering — Music discovery, Software engineering | [RSS/Atom](<https://www.earlevel.com/main/feed/>) | rss · 10 · 168 h | 10 | 2026-10-18 02:51:27 | 2026-10-25 02:51:27 | Dormant since January 2023; deliberately retained evergreen DSP, polled weekly. |
| Embodied robot learning — abstract watch — Frontier AI | `"embodied intelligence" AND robotics` | openalex · 6 · 168 h | 6 | 2026-10-18 02:50:36 | 2026-10-25 02:50:36 | Without an API key each refresh samples only the newest page; busy queries can miss papers. Configure OPENALEX_API_KEY for continuation. Two-year initial window; seven-day incremental overlap. |
| Essentia Foundation — Consciousness & philosophy | [RSS/Atom](<https://www.essentiafoundation.org/feed/>) | rss · 10 · 48 h | 10 | 2026-10-18 02:50:36 | 2026-10-20 02:50:36 | Feed-supplied text only; link to publisher for remaining content.  |
| Felix Rieseberg — Software engineering | [RSS/Atom](<https://felixrieseberg.com/rss/>) | rss · 8 · 48 h | 8 | 2026-10-18 02:50:36 | 2026-10-20 02:50:36 | Feed-supplied text only; link to publisher for remaining content.  |
| Google DeepMind — Frontier AI | [RSS/Atom](<https://deepmind.google/blog/rss.xml>) | rss · 8 · 24 h | 8 | 2026-10-18 02:50:37 | 2026-10-19 02:50:37 | Feed-supplied text only; link to publisher for remaining content.  |
| Google Research — Frontier AI | [RSS/Atom](<https://research.google/blog/rss/>) | rss · 8 · 24 h | 8 | 2026-10-18 02:50:37 | 2026-10-19 02:50:37 | Feed-supplied text only; link to publisher for remaining content.  |
| Hugging Face — Frontier AI | [RSS/Atom](<https://huggingface.co/blog/feed.xml>) | rss · 8 · 24 h | 8 | 2026-10-18 02:50:37 | 2026-10-19 02:50:37 | Feed-supplied text only; link to publisher for remaining content.  |
| IIT and global workspace — title watch — Consciousness & philosophy | `title:("integrated information theory" OR "global workspace") NOT "E8 Intelligence Research"` | openalex · 5 · 168 h | 5 | 2026-10-18 02:50:37 | 2026-10-25 02:50:37 | Without an API key each refresh samples only the newest page; busy queries can miss papers. Configure OPENALEX_API_KEY for continuation. Two-year initial window; seven-day incremental overlap. |
| Institute of Noetic Sciences — Parapsychology | [RSS/Atom](<https://noetic.org/feed/>) | rss · 10 · 48 h | 10 | 2026-10-18 02:50:38 | 2026-10-20 02:50:38 | Feed-supplied text only; link to publisher for remaining content.  |
| Journal of Scientific Exploration — Parapsychology | [RSS/Atom](<https://journalofscientificexploration.org/index.php/jse/gateway/plugin/WebFeedGatewayPlugin/atom>) | rss · 12 · 168 h | 12 | 2026-10-18 02:50:38 | 2026-10-25 02:50:38 | Feed-supplied text only; link to publisher for remaining content.  |
| Martin Fowler — Software engineering | [RSS/Atom](<https://martinfowler.com/feed.atom>) | rss · 8 · 48 h | 8 | 2026-10-18 02:51:27 | 2026-10-20 02:51:27 | Feed-supplied text only; link to publisher for remaining content.  |
| Near-death experiences — title watch — Parapsychology | `title:"near death experience"` | openalex · 5 · 168 h | 5 | 2026-10-18 02:50:39 | 2026-10-25 02:50:39 | Without an API key each refresh samples only the newest page; busy queries can miss papers. Configure OPENALEX_API_KEY for continuation. Two-year initial window; seven-day incremental overlap. |
| Panpsychism and idealism — title watch — Consciousness & philosophy | `title:(panpsychism OR "analytic idealism") NOT "E8 Intelligence Research"` | openalex · 5 · 168 h | 5 | 2026-10-18 02:50:40 | 2026-10-25 02:50:40 | Without an API key each refresh samples only the newest page; busy queries can miss papers. Configure OPENALEX_API_KEY for continuation. Two-year initial window; seven-day incremental overlap. |
| Quanta Magazine — Science & psychology | [RSS/Atom](<https://www.quantamagazine.org/feed/>) | rss · 3 · 24 h | 3 | 2026-10-17 21:18:54 | 2026-10-18 21:18:54 | Existing limit of 3 and 24-hour interval preserved. |
| Simon Willison — Software engineering | [RSS/Atom](<https://simonwillison.net/atom/everything/>) | rss · 10 · 24 h | 10 | 2026-10-18 02:50:40 | 2026-10-19 02:50:40 | Feed-supplied text only; link to publisher for remaining content.  |
| The Black Vault UFOs — Speculative history & mysteries | [RSS/Atom](<https://www.theblackvault.com/documentarchive/category/the-fringe/ufo-phenomena/feed/>) | rss · 10 · 48 h | 10 | 2026-10-18 02:50:40 | 2026-10-20 02:50:40 | Feed-supplied text only; link to publisher for remaining content.  |
| The Debrief — Speculative history & mysteries, Science & psychology | [RSS/Atom](<https://thedebrief.org/feed/>) | rss · 10 · 24 h | 10 | 2026-10-18 02:50:41 | 2026-10-19 02:50:41 | Feed-supplied text only; link to publisher for remaining content.  |
| The Splintered Mind — Consciousness & philosophy | [RSS/Atom](<https://eschwitz.substack.com/feed>) | rss · 12 · 48 h | 12 | 2026-10-18 02:50:41 | 2026-10-20 02:50:41 | Feed-supplied text only; link to publisher for remaining content.  |
| Younger Dryas impacts — title watch — Speculative history & mysteries | `title:"Younger Dryas" AND (impact OR catastrophe)` | openalex · 5 · 168 h | 5 | 2026-10-18 02:50:41 | 2026-10-25 02:50:41 | Without an API key each refresh samples only the newest page; busy queries can miss papers. Configure OPENALEX_API_KEY for continuation. Two-year initial window; seven-day incremental overlap. |

The full opt-in definitions, reasons, result limits, and last verification notes live in [curated-source-pack.json](../config/curated-source-pack.json). The app does not load that pack automatically. Selected individual imports are in [curated-reading-pack.json](../config/curated-reading-pack.json).

The IIT and panpsychism queries exclude the literal title phrase `E8 Intelligence Research` because preview pages were dominated by repetitive serial notes/extractions. This is an explicit, editable repetition control, not a rejection of idealism, panpsychism, speculative research, or a blanket publisher ban.

## Content summary

Topic counts overlap: a record may belong to several interests. “Eligible” excludes read/dismissed records and records whose known subscriptions are all disabled. Retained catalog counts intentionally include the failed-query results so preservation is visible.

| Topic | Retained catalog | Eligible for Discover |
|---|---:|---:|
| Speculative history & mysteries | 39 | 33 |
| Parapsychology | 41 | 31 |
| Music discovery | 28 | 28 |
| Frontier AI | 71 | 68 |
| Unexpected finds | 23 | 22 |
| Consciousness & philosophy | 87 | 63 |
| Software engineering | 61 | 53 |
| Science & psychology | 52 | 46 |

| Stored type | Count |
|---|---:|
| article | 173 |
| paper | 71 |
| audio | 14 |
| video | 3 |

Four audio records are actual artist SoundCloud uploads; the other ten are feed articles with audio enclosures/demos. Three are YouTube videos. Journal papers arriving through RSS are stored as articles; 71 `paper` records come through scholarly adapters. Type counts therefore describe current adapter labels, not an editorial claim that every audio record is a music track.

| Source publication age | Records |
|---|---:|
| Last 30 days | 162 |
| Older than 180 days | 40 |
| 31–180 days | 45 |
| Future source date | 1 |
| Unknown date | 13 |

One retained result, “Before Qualia, Strategic Existence Begins,” has the provider date 2026-11-01. It belongs to a retired broad watch and is excluded from Discover; its date was preserved. Dates come from source responses, never ingestion time. The Chalmers essay is known as a 1995 work but remains “Date unknown” because its page supplied no machine-readable publication timestamp. Dates attached to historical archive webpages are distinguished from the dates of their underlying events. OpenAlex date precision is provider-reported, including January 1 values where present.

## Editorial audit

Inspected **45 actual records** across feeds, scholarly records, manual references, video, and music: title, canonical link, creator, publication value, available excerpt/body, topic assignments, related explanation/evidence, reader representation, type, and duplicate identity. This is an assessment of personal fit, not whether a source’s claims agree with a worldview.

Reader codes: **feed text** = supplied plain text available locally; **abstract** = actual provider abstract; **metadata** = excerpt/link only; **link fallback** = explicit extraction warning. Empty creator/date fields remain unknown. Both live media providers displayed the correct original player; audible playback and exact musical timbre were not evaluated.

| Actual record / original link | Creator; source date | Type / reader | Personal relevance | Editorial finding |
|---|---|---|---|---|
| [Grounding AI agents in biological reality: Building blocks for autonomous wet labs](<https://research.google/blog/grounding-ai-agents-in-biological-reality-building-blocks-for-autonomous-wet-labs/>) | Unknown; 2026-10-16 | article / metadata | Excellent fit | Original lab work connecting agents with autonomous wet labs. Feed supplies only a category teaser; full article stays at the source. |
| [Gemini 4 Argon for Robotics](<https://deepmind.google/blog/gemini-4-argon-for-robotics/>) | Unknown; 2026-10-13 | article / metadata | Excellent fit | Direct robotics release, but title-only metadata makes automatic branching less specific. |
| [Teaching LLMs to Update Beliefs for Efficient Long-Horizon Interaction](<http://bair.berkeley.edu/blog/2026/07/26/abbel/>) | Unknown; 2026-07-26 | article / metadata | Excellent fit | Long-horizon agent memory and belief supervision. CSS was stripped and the real supplied summary was reimported. |
| [How to be more ambitious: A tutorial for humans with models](<https://felixrieseberg.com/how-to-be-more-ambitious-a-tutorial-for-humans-with-models/>) | Felix Rieseberg; 2026-10-13 | article / feed text | Excellent fit | Practitioner account of model-assisted work; full feed text supports reading locally. |
| [Redesigning source control at Anthropic](<https://simonwillison.net/2026/Oct/13/redesigning-source-control-at-anthropic/>) | Unknown; 2026-10-13 | article / metadata | Excellent fit | Concrete version-control tradeoffs at high agent-generated change volume; commentary retains the original attribution. |
| [A global workspace in language models](<https://www.anthropic.com/research/global-workspace>) | Unknown; 2026-07-06 | article / metadata | Excellent fit | Connects interpretability with a specific consciousness theory. A workspace claim is not presented as proof of experience. |
| [Emergent introspective awareness in LLMs](<https://www.anthropic.com/research/introspection>) | Unknown; 2025-10-29 | article / metadata | Excellent fit | Direct experimental AI introspection research; metadata/excerpt only. |
| [Better prompt caching for GPT-6](<https://openai.com/index/better-prompt-caching-for-gpt-6>) | Unknown; 2026-09-22 | article / metadata | Excellent fit | Specific engineering controls and latency/cost tradeoffs. Exact entry selected from official RSS after page access failed. |
| [Facing Up to the Problem of Consciousness](<https://consc.net/papers/facing.html>) | David J. Chalmers; unknown | article / metadata | Excellent fit | Foundational hard-problem essay on the author’s site. Author verified; absent machine-readable date remains unknown (the essay is from 1995). |
| [What Is It Like to Be a Bat?](<https://doi.org/10.2307/2183914>) | Thomas Nagel; 1974-10-01 | paper / abstract | Excellent fit | Original 1974 argument about subjective experience, with provider-supplied abstract. |
| [Epiphenomenal Qualia](<https://doi.org/10.2307/2960077>) | Frank Jackson; 1982-04-01 | paper / metadata | Excellent fit | Original 1982 knowledge argument; no abstract was invented for the metadata-only record. |
| [The Appeal of (Semi-?) Biological Naturalism about Consciousness](<https://eschwitz.substack.com/p/the-appeal-of-semi-biological-naturalism>) | Eric Schwitzgebel; 2026-10-02 | article / feed text | Excellent fit | Current philosopher’s essay suited to comparison with the separate idealist material, without forcing a comparison. |
| [Nature in consciousness](<https://www.essentiafoundation.org/nature-in-consciousness/reading/>) | Boy van Droffelaar, PhD; 2026-09-18 | article / feed text | Excellent fit | An explicit non-materialist perspective with named author and feed-supplied text. |
| [Is everything conscious?](<https://aeon.co/essays/does-the-principle-of-sufficient-reason-make-panpsychism-true>) | Sam Kimpton-Nye; 2026-10-12 | article / metadata | Excellent fit | Panpsychism and sufficient reason. Added a philosophy tag that the broad feed assignment missed. |
| [The flood story — Sumerian text translation (ETCSL 1.7.4)](<https://etcsl.orinst.ox.ac.uk/cgi-bin/etcsl.cgi?text=t.1.7.4>) | Unknown; unknown | article / metadata | Excellent fit | Original Sumerian translation archive. Link-only reading; no modern publication date substituted for the ancient narrative. |
| [Large Pt anomaly in the Greenland ice core points to a cataclysm at the onset of Younger Dryas](<https://doi.org/10.1073/pnas.1303924110>) | Michail I. Petaev, Shichun Huang, S. B. Jacobsen, Alan Zindler; 2013-07-22 | paper / abstract | Excellent fit | Original measured platinum anomaly and the authors’ catastrophe interpretation; retains original DOI and date. |
| [Abu Hureyra, Syria, Part 3: Comet airbursts triggered major climate change 12,800 years ago that initiated the transition to agriculture](<https://doi.org/10.14293/aci.2023.0004>) | Andrew M. T. Moore, James P. Kennett, W. M. Napier, Malcolm A. LeCompte, Christopher R. Moore, Allen West; 2023-01-01 | paper / abstract | Excellent fit | Primary alternative-catastrophe argument linking impact claims and agriculture; preserved as the authors’ hypothesis. |
| [The Late Pleistocene Catastrophe: A Cross-Disciplinary Framework Aligning Younger Dryas Impact Evidence, Postglacial Flood Geology, and Oral Tradition Methodology](<https://doi.org/10.5281/zenodo.20696438>) | Paul Alfred Oldridge; 2026-06-15 | paper / abstract | Interesting / acceptable | An unusual bridge between flood traditions and geology. Early framework/preprint metadata, not a settled causal connection. |
| [Project BLUE BOOK — Unidentified Flying Objects](<https://www.archives.gov/research/military/air-force/ufos>) | Unknown; 2016-08-15 | article / metadata | Excellent fit | Historical archive entry with original links. The displayed 2016 date belongs to the webpage, not the historical investigations. |
| [Newly Released Documents Show UAP “Space Tiger Team” Built Around Space and Transmedium Cases](<https://www.theblackvault.com/documentarchive/newly-released-documents-show-uap-space-tiger-team-built-around-space-and-transmedium-cases/?utm_source=rss&utm_medium=rss&utm_campaign=newly-released-documents-show-uap-space-tiger-team-built-around-space-and-transmedium-cases>) | John Greenewald; 2026-05-01 | article / feed text | Excellent fit | FOIA-based UAP reporting, named investigator and source documents; concrete rather than a generic sensational roundup. |
| [The Urban Relict Hominoid: An Analysis of Incursions into Populated Areas](<https://journalofscientificexploration.org/index.php/jse/article/view/3993>) | Bruce Champagne; 2026-09-29 | article / metadata | Excellent fit | Specific cryptid investigation with a stated sample and method. Added the mysteries tag; RSS adapter labels it article. |
| [Dorothy Eady/Omm Sety (reincarnation case)](<https://psi-encyclopedia.spr.ac.uk/articles/dorothy-eadyomm-sety-reincarnation-case/>) | Unknown; unknown | article / metadata | Excellent fit | Firsthand-life/reincarnation case material connecting Egypt and anomalous experience; no extracted body or date available. |
| [Information transmission under conditions of sensory shielding](<https://doi.org/10.1038/251602a0>) | Russell Targ, H. E. Puthoff; 1974-10-01 | paper / metadata | Excellent fit | Original 1974 Targ/Puthoff experiment report; bibliographic metadata only, with no invented abstract. |
| [Remote Viewing — Psi Encyclopedia](<https://psi-encyclopedia.spr.ac.uk/articles/remote-viewing/>) | Unknown; unknown | article / metadata | Excellent fit | Specialist reference starting point. Sparse metadata limits lexical links; no unsupported experimental summary added. |
| [The perceptual geometry and chromaticity of near-death experience spaces](<https://doi.org/10.3389/fpsyg.2026.1760389>) | France Lerner, Paulo Cezar Rocha dos Santos, Guillaume Tahar; 2026-10-15 | paper / abstract | Excellent fit | Specific NDE investigation using graphic reconstructions. Multiple watches converge on one record through identifiers. |
| [Investigating Remote Electrogastrogram Modulation Via Emotional and Eating Stimuli: A Sham-Controlled Pilot Study](<https://journalofscientificexploration.org/index.php/jse/article/view/3901>) | Garret Yount, Arnaud Delorme, Helané Wahbeh, Jessica Haro Lopez, Sitara Taddeo, Michael Kriegsman, Salma Ahmadzai, Richard Tian, et al.; 2026-09-29 | article / metadata | Excellent fit | A defined sham-controlled pilot for anomalous influence, with available source summary and attribution. |
| [Durable Task in the Real World: What Problems Does It Actually Solve?](<https://devblogs.microsoft.com/dotnet/durable-task-in-the-real-world/>) | Tamir Dresher; 2026-10-12 | article / feed text | Excellent fit | Advanced execution/reliability decisions. Removed incidental philosophy/serendipity tags from this newly added record. |
| [Creating a memory dump in C#](<https://devblogs.microsoft.com/dotnet/creating-a-memory-dump-in-csharp/>) | Aaron Powell; 2026-09-22 | article / feed text | Excellent fit | Practical .NET debugging internals with real local feed text. |
| [Are Harnesses Worth the Cost?](<https://martinfowler.com/articles/are-harnesses-worth-the-cost.html>) | Martin Fowler; 2026-10-13 | article / feed text | Excellent fit | Concrete coding-agent evaluation and cost tradeoffs. Feed attributes Martin Fowler and explicitly describes Naresh Jain’s experiment; removed email/URI from the Atom creator field. |
| [Biquad calculator v3](<https://www.earlevel.com/main/2021/09/02/biquad-calculator-v3/>) | Nigel Redmon; 2021-09-02 | article / feed text | Excellent fit | Reusable DSP/filter-design material, accurately dated 2021; relevant adjacent production and engineering paths. |
| [Time resolution in digital audio](<https://www.earlevel.com/main/2021/04/08/time-resolution-in-digital-audio/>) | Nigel Redmon; 2021-04-09 | article / feed text | Excellent fit | Technical sampling/timing explanation suitable for an experienced producer/developer. |
| [FL STUDIO \\| Sytrus](<https://www.image-line.com/fl-studio-news/sytrus-tutorial>) | FL Studio; 2024-01-17 | article / metadata | Excellent fit | Official FL Studio synthesis resource, with source-reported date and creator. |
| [Tips for Riddim Sound Design & Introduction to Vital w/ INFEKT](<https://www.youtube.com/watch?v=D-n9zU730Sc>) | Soundteams; unknown | video / metadata | Excellent fit | Specific INFEKT/Vital breakdown from Soundteams. Real YouTube player loaded; no captions or transcript were fetched. |
| [Bitwig 1-Minute Patch: The Smallest Generative Patch](<https://www.youtube.com/watch?v=tHhctmNHMio>) | Bitwig; unknown | video / metadata | Interesting / acceptable | Official Bitwig modular patch; a useful production detour, though not a riddim-specific lesson. |
| [INFEKT - SECTOR 9](<https://soundcloud.com/infektdubstep/infekt-sector-9>) | INFEKT; unknown | audio / metadata | Interesting / acceptable | Verified INFEKT artist upload and original SoundCloud player. Artist/era matches the requested direction; exact sonic fit was not auditioned. |
| [Blockz (2025) (Pl8list Exclusive)](<https://soundcloud.com/subfiltronik/blockz-2025-pl8list-exclusive>) | SUBFILTRONIK; unknown | audio / metadata | Interesting / acceptable | Verified Subfiltronik upload; a newer comparison track, with no invented publish date or listening-based style claim. |
| [How Do Fungi Keep the Planet Healthy?](<https://www.quantamagazine.org/how-do-fungi-keep-the-planet-healthy-20261015/>) | Steven Strogatz and Janna Levin; 2026-10-15 | article / feed text | Excellent fit | Science/biology serendipity, grounded in an actual Quanta interview; preserved original item unchanged. |
| [13,800-Year-Old Alaskan ‘Base Camp’ Yields New Clues to Ancient Beringia Land Bridge Mystery](<https://thedebrief.org/13800-year-old-alaskan-base-camp-yields-new-clues-to-ancient-beringia-land-bridge-mystery/>) | Christopher Plain; 2026-10-17 | article / feed text | Excellent fit | Archaeological migration puzzle adjacent to ancient-history interests, with named author and real feed text. |
| [Tactile-Guided Robot Learning: From Representation Learning to Foundation Models](<https://arxiv.org/abs/2610.20134>) | Kiyanoush Nazari, Luchen Li, Haoshu Fang, Nima Fazeli, Zhibin Li, Fumiya Iida A, Thomas George Thuruthel; 2026-10-14 | paper / abstract | Excellent fit | Substantive robotics survey spanning sensing, representations, and embodied learning; supplied abstract supports richer branches. |
| [Sickle cell retinopathy molecular investigation through proteomic analysis of Townes HbSS mice retinas](<https://doi.org/10.3389/ebm.2026.11228>) | Júlia Nicoliello Pereira de Castro, Ana Rafaela Carvalho Monte, Ana Carolina Rodrigues de Oliveira, Julia Dezordi Acacio, Thiago Adalton Rosa Rodrigues, Mirella Telles Salgueiro Barboni, André Maurício Passos Liber, Daniela Maria Oliveira Bonci, et al.; 2026-10-14 | paper / abstract | Off-topic or unusable | Unrelated medical result from the original broad psi watch. That new watch is disabled; record retained in Search. |
| [Assessing the Impact of Integrated Information Systems on Operational Efficiency in Indian Salon SMEs](<https://doi.org/10.22214/ijraset.2026.85148>) | B.  Gnanasankari; 2026-10-15 | paper / abstract | Off-topic or unusable | Phrase ambiguity confused business information systems with IIT. Tightened query to integrated information theory and passed on this new record. |
| [Some Twitch chats exhibit multifractal characteristics resembling stream-of-consciousness narrative](<https://arxiv.org/abs/2610.10654v1>) | Marcin Ból; 2026-10-07 | paper / metadata | Weak fit | Interesting complex-systems work, but weak for phenomenal consciousness. Original record/state and its already-disabled source were preserved. |
| [Anthropic tells investors quarterly revenue up nearly 50% to $16.8bn ahead of IPO](<https://simonwillison.net/2026/Oct/15/anthropic-tells-investors/>) | Unknown; 2026-10-15 | article / metadata | Weak fit | Financial publicity is outside the requested technical focus. Passed on this newly imported item; kept the otherwise useful engineering feed. |
| [Announcements](<https://journalofscientificexploration.org/index.php/jse/article/view/4155>) | Editorial Office; 2026-09-29 | article / metadata | Weak fit | Issue administration without a substantive excerpt. Passed on the new item; retained the journal’s actual research entries. |
| [Reincarnation: An Introduction — Psi Encyclopedia](<https://psi-encyclopedia.spr.ac.uk/articles/reincarnation-overview/>) | Unknown; unknown | article / link fallback | Interesting / acceptable | Relevant reference retained as an honest link-only item after the size boundary rejected extraction; the original remains linked for reading outside the app. |

Audit classifications: 35 excellent fit, 5 interesting / acceptable, 2 off-topic or unusable, 3 weak fit.

The FinTech information-systems result was a second instance of the IIT phrase ambiguity. It was also passed on after the 45-record sample. The IIT query now requires the full phrase `integrated information theory`. Initial medical/figurative matches remain available in Search through their disabled original watches. Two distinct identifiers with the same “Algorithmic Pathology” title remain as separate retained records from a disabled watch; they are not promoted as two different discoveries.

Metadata repairs were verified by forced real refreshes: the BAIR belief-state article now begins with the actual summary instead of CSS, and the Fowler Atom author now contains the name instead of name/email/URI concatenation. Sparse metadata still limits several older references and lab announcements. No source excerpt or summary was invented to fill those gaps.

## Daily edition

The final edition contains 25 real items. An additional 25 were requested and checked for duplicate IDs, then the edition was deliberately regenerated back to 25. Repeating regeneration with unchanged catalog/state produced the same ordering, as intended for the daily deterministic seed. This checks two selections in one day; it is not a claimed observation over multiple calendar days.

| Position | Actual selected item | Why this? |
|---:|---|---|
| 1 | [Welcome Beam: a 501B open model you can own, customize, and scale](<https://huggingface.co/blog/reflection-beam>) | Connected to something you explored |
| 2 | [A Neuro-Functional Framework of Patanjali's Ashtanga Yoga: Mapping Ancient Mind Modulation Systems to Global Workspace Theory](<https://doi.org/10.5281/zenodo.23393432>) | Connected to something you explored |
| 3 | [Inline rendering with document​.currentScript](<https://simonwillison.net/2026/Oct/17/document-currentscript/>) | Connected to something you explored |
| 4 | [From Intelligent Control to Integrated Autonomy: Reflections on Advanced Intelligent Robotics and Autonomous Control Systems](<https://doi.org/10.3390/s26206460>) | Connected to something you explored |
| 5 | [Giving One’s Life](<https://eschwitz.substack.com/p/giving-ones-life>) | Connected to something you explored |
| 6 | [Going Off Grid: What Is Swing And How To Add It](<https://www.attackmagazine.com/technique/tutorials/going-off-grid-what-is-swing-and-how-to-add-it/>) | Connected to something you explored; an older item worth revisiting |
| 7 | [An introduction to Dynamic Emergence Algebra: Context-driven operators for optimizing Integrated Information Theory computational limits](<https://doi.org/10.5281/zenodo.23176285>) | Connected to something you explored |
| 8 | [He Wants AI Benchmarking To Be a Science, Not a Dark Art](<https://www.quantamagazine.org/he-wants-ai-benchmarking-to-be-a-science-not-a-dark-art-20261016/>) | Connected to something you explored |
| 9 | [.NET and .NET Framework October 2026 servicing releases updates](<https://devblogs.microsoft.com/dotnet/dotnet-and-dotnet-framework-october-2026-servicing-updates/>) | Connected to something you explored |
| 10 | [Locality of integrated information](<https://doi.org/10.5281/zenodo.23298173>) | Connected to something you explored |
| 11 | [A listening test](<https://www.earlevel.com/main/2021/11/04/a-listening-test/>) | Connected to something you explored; an older item worth revisiting |
| 12 | [Google DeepMind and Singapore Government collaborate to build fine-tuned AI model for scam detection](<https://deepmind.google/blog/on-device-ai-scam-detection-mddi/>) | Connected to something you explored |
| 13 | [When Software Meets Hardware](<https://martinfowler.com/articles/software-meets-hardware.html>) | Connected to something you explored |
| 14 | [Bitwig 1-Minute Patch: Reese Bass / Rave Lead](<https://www.youtube.com/watch?v=R_l2rsSdWgw>) | Connected to something you explored |
| 15 | [The Near-Death Experience, an Instance of Higher-Dimensional Phenomenal Consciousness?](<https://doi.org/10.53765/20512201.33.9.111>) | Connected to something you explored |
| 16 | [What exactly does word2vec learn?](<http://bair.berkeley.edu/blog/2025/09/01/qwem-word2vec-theory/>) | Connected to something you explored; an older item worth revisiting |
| 17 | [80s-Style Tuned Percussion With Zebra 3](<https://www.attackmagazine.com/technique/synth-secrets/80s-style-tuned-percussion-with-zebra-3/>) | Connected to something you explored; an older item worth revisiting |
| 18 | [13,800-Year-Old Alaskan ‘Base Camp’ Yields New Clues to Ancient Beringia Land Bridge Mystery](<https://thedebrief.org/13800-year-old-alaskan-base-camp-yields-new-clues-to-ancient-beringia-land-bridge-mystery/>) | From your interest in Speculative history & mysteries |
| 19 | [The perceptual geometry and chromaticity of near-death experience spaces](<https://doi.org/10.3389/fpsyg.2026.1760389>) | A serendipitous find from your sources |
| 20 | [Putting Earth AI to work: Geospatial agents for our planet's biggest challenges](<https://research.google/blog/putting-earth-ai-to-work-geospatial-agents-for-our-planets-biggest-challenges/>) | Connected to something you explored |
| 21 | [Structural Potentiality of Consciousness- A Matter-to-Mind Framework Compatible with Integrated Information Theory.](<https://doi.org/10.5281/zenodo.23408161>) | Connected to something you explored |
| 22 | [Avoid Git Merge Conflicts During a Big Rebase](<https://felixrieseberg.com/better-rebasing/>) | Connected to something you explored; an older item worth revisiting |
| 23 | [The Late Pleistocene Catastrophe: A Cross-Disciplinary Framework Aligning Younger Dryas Impact Evidence, Postglacial Flood Geology, and Oral Tradition Methodology](<https://doi.org/10.5281/zenodo.20696438>) | From your interest in Speculative history & mysteries |
| 24 | [MARS: Systematic Multi-Level Acceleration for World Action Models](<https://arxiv.org/abs/2610.17141>) | Connected to something you explored |
| 25 | [Navy Releases Massive Cache of Scott Bray UAP Emails After Four Years; More Than 300 Pages Withheld in Full](<https://www.theblackvault.com/documentarchive/navy-releases-massive-cache-of-scott-bray-uap-emails-after-four-years-more-than-300-pages-withheld-in-full/?utm_source=rss&utm_medium=rss&utm_campaign=navy-releases-massive-cache-of-scott-bray-uap-emails-after-four-years-more-than-300-pages-withheld-in-full>) | From your interest in Speculative history & mysteries |

The selection spans source families and includes older engineering/music material. It still admits some release/service announcements, and the lead card can have sparse metadata. Topic continuity uses saved trails as evidence; a connection rationale is a navigation hint, not a claim of demonstrated knowledge. Topic weights, freshness (50%), serendipity (15%), and edition size (25) were preserved.

## Rabbit-hole evidence

Inspected three recommendation levels for each of the six requested categories, plus a richer robotics starting paper. At each level the saved audit contains all 20 candidates, real evidence IDs, and the selected next record. The walk below chooses the first unvisited recommendation; avoiding previously visited nodes was an audit choice, not an implemented cycle filter.

**AI consciousness**

[A global workspace in language models](<https://www.anthropic.com/research/global-workspace>) → [Content Without Access: A Cross-Family Fleet Measurement of Global Workspace Properties in Transformer Language Models](<https://doi.org/10.5281/zenodo.23165932>) → [Psychedelics Restructure the Global Workspace of Consciousness](<https://doi.org/10.64898/2026.09.28.755172>) → [A Neuro-Functional Framework of Patanjali's Ashtanga Yoga: Mapping Ancient Mind Modulation Systems to Global Workspace Theory](<https://doi.org/10.5281/zenodo.23393432>)

Good bridge from model interpretability to global-workspace hypotheses and altered states. These are distinct source assertions, not evidence that all the proposed mechanisms are established. Geospatial “global/models” still appears as a weak sibling despite the much better first result.

**Ancient flood myths**

[The flood story — Sumerian text translation (ETCSL 1.7.4)](<https://etcsl.orinst.ox.ac.uk/cgi-bin/etcsl.cgi?text=t.1.7.4>) → [The Late Pleistocene Catastrophe: A Cross-Disciplinary Framework Aligning Younger Dryas Impact Evidence, Postglacial Flood Geology, and Oral Tradition Methodology](<https://doi.org/10.5281/zenodo.20696438>) → [Identification of a New England Bolide Impact Site: A Geologic Reckoning with the Ground-Zero for the Younger Dryas Impact Event](<https://doi.org/10.5281/zenodo.19146481>) → [A spatial gradient without a radial signature: a county-level test of the Younger Dryas impact hypothesis demographic prediction in PIDBA (analysis code and data)](<https://doi.org/10.5281/zenodo.19633104>)

First step is a broad-topic detour because the historical translation has no excerpt. The later Younger Dryas links are specific. Some siblings remain unrelated UFO/radar material because mysteries is a broad topic.

**UFO history**

[Project BLUE BOOK — Unidentified Flying Objects](<https://www.archives.gov/research/military/air-force/ufos>) → [Israeli Military Continues the Search for an Unidentified Aerial Object Detected Near Lebanese Border](<https://thedebrief.org/israeli-military-continues-the-search-for-an-unidentified-aerial-object-detected-near-lebanese-border/>) → [Navy Releases Massive Cache of Scott Bray UAP Emails After Four Years; More Than 300 Pages Withheld in Full](<https://www.theblackvault.com/documentarchive/navy-releases-massive-cache-of-scott-bray-uap-emails-after-four-years-more-than-300-pages-withheld-in-full/?utm_source=rss&utm_medium=rss&utm_campaign=navy-releases-massive-cache-of-scott-bray-uap-emails-after-four-years-more-than-300-pages-withheld-in-full>) → [New Emails Reveal Pentagon Effort to Align Messaging on AATIP and Luis Elizondo](<https://www.theblackvault.com/documentarchive/new-emails-reveal-pentagon-effort-to-align-messaging-on-aatip-and-luis-elizondo/?utm_source=rss&utm_medium=rss&utm_campaign=new-emails-reveal-pentagon-effort-to-align-messaging-on-aatip-and-luis-elizondo>)

Archive → recent unidentified-object reporting → FOIA email records is plausible. The first sibling is a current military report, so it is not automatically a close historical case comparison.

**Parapsychology**

[Remote Viewing — Psi Encyclopedia](<https://psi-encyclopedia.spr.ac.uk/articles/remote-viewing/>) → [Reincarnation: An Introduction — Psi Encyclopedia](<https://psi-encyclopedia.spr.ac.uk/articles/reincarnation-overview/>) → [Dorothy Eady/Omm Sety (reincarnation case)](<https://psi-encyclopedia.spr.ac.uk/articles/dorothy-eadyomm-sety-reincarnation-case/>) → [Exploring the History of Reincarnation Beliefs](<https://noetic.org/blog/history-of-reincarnation/>)

Reincarnation case/history is a useful adjacent subject, but sparse Psi Encyclopedia metadata makes most rationales broad-topic hints. The size-limited overview remains link-only.

**Robotics**

[Gemini 4 Argon for Robotics](<https://deepmind.google/blog/gemini-4-argon-for-robotics/>) → [Developing Gemini 4 Argon responsibly and safely](<https://deepmind.google/blog/developing-gemini-4-argon-responsibly-and-safely/>) → [Gemini 4 Argon: our next era of frontier intelligence](<https://deepmind.google/blog/gemini-4-argon-our-next-era-of-frontier-intelligence/>) → [llm-gemini 0.35](<https://simonwillison.net/2026/Oct/13/llm-gemini/>)

Weak chain: shared model branding keeps this title-only starting item among release/safety/plugin posts. Source caps add alternatives but cannot infer missing robotics details.

**Audio DSP**

[Biquad calculator v3](<https://www.earlevel.com/main/2021/09/02/biquad-calculator-v3/>) → [Inter-sample peaks](<https://www.earlevel.com/main/2023/01/16/inter-sample-peaks/>) → [AM widget](<https://www.earlevel.com/main/2020/10/02/am-widget/>) → [Amplitude Modulation Principles and Interactive Widget video](<https://www.earlevel.com/main/2020/10/29/amplitude-modulation-principles-and-interactive-widget-video/>)

The corrected chain connects filter design, sample peaks, modulation, and drones. It no longer starts with .NET servicing because of an incidental “version” match. Some sound-design detours are broader than riddim.

**Robotics (research starting point)**

[Tactile-Guided Robot Learning: From Representation Learning to Foundation Models](<https://arxiv.org/abs/2610.20134>) → [What exactly does word2vec learn?](<http://bair.berkeley.edu/blog/2025/09/01/qwem-word2vec-theory/>) → [Gradient-based Planning for World Models at Longer Horizons](<http://bair.berkeley.edu/blog/2026/04/20/grasp/>) → [Thinker-WM: A Unified World Model with Shared Multimodal Representations for Embodied Intelligence](<https://arxiv.org/abs/2610.18447>)

More useful: a real tactile-learning abstract leads through representation learning and planning to another embodied world-model paper. Broad technical overlap remains a heuristic.

Two curated, editable trails are saved in the running app: [From AI workspaces to altered states](http://localhost:3000/holes/0cd21a8f-9914-4775-bf31-5c241c0019fa) and [From filter design to sound design](http://localhost:3000/holes/0780c86f-cd9c-473c-b5d8-fab922620dbe). Each has a root, three levels, and a sibling branch. API navigation and the live browser confirmed backtracking, sibling preservation, and stored current position; all ten nodes survived the database/API/web restart.

## Operations, restrictions, and unavailable sources

All 23 enabled sources have a successful retrieval and no current error. There are 35 stored subscriptions total: 23 enabled and 12 disabled. The disabled total includes the two pre-existing research watches, six newly retired broad watches, two new arXiv watches, Variety of Sound, and the empty focused psi watch. Disabled sources do not run on their displayed next-due timestamp.

- **OpenAlex:** no key is configured. Initial successful retrievals exposed HTTP 401 on continuation. The app now explicitly runs bounded newest-page samples without a key; Settings explains that busy queries can miss papers. With a key, fixed-window cursor continuation and restart recovery remain intact. Five watches now refresh successfully in this mode. Initial backfill is limited to two years; subsequent windows overlap seven days.
- **arXiv:** focused consciousness/robotics watches returned timeouts and HTTP 429; left disabled. The 2023 Butlin report (`2308.08708`) returned HTTP 502 on two import attempts and is recorded under unavailable selections, not falsely imported. “Attention Is All You Need” did import successfully once; its first import had one author, and API reimport later failed. The author list was then corrected from the [original arXiv page](https://arxiv.org/abs/1706.03762), retaining all eight named authors. New Atom parsing retains multiple named authors when available.
- **Variety of Sound:** external read-only probe parsed its feed, but the production fetcher rejected a private/reserved DNS result. The source remains disabled; the SSRF boundary was not bypassed.
- **Brains Blog:** certificate validation failed; no insecure bypass or active subscription.
- **Anthropic:** three verified individual research URLs, without an invented RSS endpoint or promised automatic coverage.
- **OpenAI:** official feed verified; selected three technical entries instead of subscribing to the broader promotional feed. Publisher-page HTTP 403 was handled through exact matching real feed records.
- **Psi Encyclopedia / SPR:** individual real reference URLs, not an invented automated monitor. The reincarnation overview exceeded the metadata fetch size limit and visibly retains a link-only warning.
- **Daily Grail, Daily Nous, MusicTech:** inspected, but recent roundup/professional-announcement/deal-heavy selections were not activated. The attempted Daily Grail strange-history category URL returned 404. The Anomalist had no verified feed in this pass.
- **Music:** four original artist tracks and three verified instructional videos. No unrestricted SoundCloud search, captions scraping, or media downloads. AD, 50 Carrot, Spass, and Ayonikz remain taste seeds, not invented source subscriptions.
- **No AI credentials:** the final system status reports AI, OpenAlex key, and YouTube API credentials absent. Browsing, search, deterministic ranking, local reading, trails, and known-URL embeds were tested in that configuration.

Provider contracts were checked against official [OpenAlex search](https://help.openalex.org/api/searching/), [authentication](https://help.openalex.org/api/authentication/), [arXiv query syntax](https://info.arxiv.org/help/api/user-manual.html), and [SoundCloud oEmbed](https://developers.soundcloud.com/docs/oembed) documentation. Live availability still governs each refresh; the report does not guarantee future provider access.

## Verification and preservation

| Actual command / check | Result |
|---|---|
| `dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release --no-restore -v:minimal --blame-hang-timeout 60s` | **130 passed**, 0 failed, 0 skipped in the final run. An initial phrase-weight regression failed; the weighting was corrected and the complete suite rerun. |
| `dotnet format apps/api/RabbitHole.Api.csproj --verify-no-changes --no-restore` | Passed. |
| `npm --prefix apps/web run lint` | Passed. |
| `npm --prefix apps/web run typecheck` | Passed. |
| `npm --prefix apps/web run build` | Passed; all app routes built. |
| `npm run test:e2e` | **14 passed** in 40.2 seconds, using isolated clearly marked fixtures and a separate database. Includes save/read reload, notes, branching, documents, media gating, failure handling, and mobile overflow/navigation. |
| `docker compose up --build -d --wait` | Full production build/start passed; later API-only rebuild deployed the final ranking/text fixes. |
| `docker compose restart db api web` then `docker compose up -d --wait` | All four services healthy. Gateway remains the only host port: `127.0.0.1:3000`. |
| Pre/post restart export comparison | Items, identities, origin/topic edges, states, notes, documents, sections, holes/nodes, practice rows, editions/entries, settings, and source preferences equal. |
| Repeat refreshes of 22 new active sources | All completed; **0 new records**, catalog **261 → 261**. Three forced normalization refreshes also imported 0. |
| Reapply source and reading packs | Preserved all 23 matching active source settings and 27 selected records; no duplicates or preference changes. |
| Canonical URL / DOI / arXiv identity scan | Zero duplicate nonempty identity values; zero TEST FIXTURE titles in production. Similar-title records with different identities are discussed above. |
| Compare original snapshot | All nine original item objects and the existing state object exactly equal; all eight topics, settings, and three original source configurations preserved. |
| Live browser | Populated Discover, correct unknown-date/source notes, SoundCloud and YouTube original players, saved trail map/backtracking/siblings, and keyless-watch guidance inspected. Audible playback not asserted. |

A consistent pre-curation database/uploads backup was created at `backups/20261017-222117`. Private raw probes, API exports, provider jobs, sampled metadata, branch candidates, edition records, and comparison results are retained under ignored `data/curation/`; they are not bundled into the reusable pack. A mistaken POST to the GET-only more-items endpoint returned 405 during QA; it was corrected, and the successful final selection check is recorded. No failed test or provider attempt is counted as a successful import.

## Reuse and opening the app

Open [Discover](http://localhost:3000) for the prepared 25-item edition. Open **Rabbit Holes** for the two saved trails, or search **INFEKT**, **remote viewing**, **Younger Dryas**, or **qualia**. Use **New selection** after future imports; ongoing subscriptions refresh on their saved schedules.

```powershell
python scripts/apply-source-pack.py             # Read-only plan
python scripts/apply-source-pack.py --apply     # Explicit opt-in; preserves existing subscriptions
python scripts/import-reading-pack.py          # Read-only plan
python scripts/import-reading-pack.py --apply   # Imports missing selected real records
```

Python 3 uses only its standard library. The app must already be running. `--base http://127.0.0.1:3000/api` can target another loopback port. Source `--only "Name"` narrows a run; `--refresh` with `--apply` explicitly refreshes matching existing sources without changing their preferences. Failed newly created sources remain disabled for inspection. Editing a pack is not a mass update of existing user sources; change existing subscriptions in Settings.

## Most valuable follow-ups

1. Add small optional subtopic tags for robot manipulation, global workspace, flood traditions, UAP archives, and filter design; broad eight-topic matches still produce weak siblings.
2. Expand original historical/occult/cryptid references and specific DJ/CDJ lessons by a few manually verified links at a time; these are thinner than AI/philosophy coverage.
3. Improve sparse publisher metadata through permitted structured metadata or user-authored annotations, preserving a separate provenance field; do not synthesize missing source abstracts.
4. Add an optional OpenAlex key for cursor coverage and revisit the disabled arXiv watches when the provider recovers; keep finite quotas and visible failures.
5. Make related navigation aware of visited trail nodes and offer lightweight per-source include/exclude terms so recurring announcement noise can be controlled without suppressing viewpoints.
