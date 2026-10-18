# Recommendation intelligence and exploration quality

Verified 18 October 2026 against the existing local application. This pass changes deterministic recommendation logic, explanations and trail provenance; it adds no content source, external integration, AI requirement or credibility policy.

## Outcome and evaluation boundary

The actual starting catalog contained **276 items**, not the approximate 261 in the request, with 35 configured sources (23 enabled), eight topics, two curated trails and ten nodes. All 276 original records and all original personal-state fields were retained. The original catalog/settings fingerprint is `c32da3b5205a6525e089003f454ceb88f8ca9de99cc6b962192bcecc3f3a23dd`.

The robotics announcement path now reaches robot learning and world models instead of repeatedly following the Gemini name. Global-workspace recommendations no longer promote an Earth AI article for the generic words “global” and “models.” Audio branches offer filters, sampling and sound design. Remote Viewing and Project Blue Book remain limited by sparse metadata and the available collection; their new connections should be judged as adjacent subjects, not precise substitutes for remote-viewing protocols or historical case analysis.

## Root cause: curated nodes became personal history

The prior `Ranking.SelectAsync` queried the latest 30 `Nodes` without an origin distinction. It gave a 0.55 continuity contribution for broad-topic overlap with those nodes, then chose “Connected to something you explored” ahead of the topic-interest explanation. The population script had created both example trails and ten nodes. Their source-topic overlap was consequently treated as evidence of personal exploration. Separately, the prior history query included negative feedback as if it could provide positive connection evidence.

This was traced in code and the real export, not inferred from the card wording. The baseline contained seven item-state rows but **no saved, read or positively rated items**. The fixed-clock replay reproduces 21 of 25 old daily explanations using the exploration phrase (five also append an older-item sentence). The two trail IDs and creation times agree with the curation script and [feed-curation-report.md](feed-curation-report.md). Existing nodes are not evidence that the user read or understood their sources.

## Evidence and provenance now

| Signal | Interpretation |
|---|---|
| Enabled topic weight and pin | Explicit interest; largest positive daily ranking input. |
| Temporary curiosity topic | Separate, capped interest contribution; not promoted to a lasting interest. |
| Saved / explicitly marked read / positive feedback | Separately named evidence, linked to actual records. Negative feedback is never positive exploration evidence. |
| User-created trail turn | Small continuity contribution from the latest 30 turns within 30 days. The explanation says “chose for a trail,” never “read” or “understood.” |
| Passive open, navigation, understood-only flag | No positive continuity contribution. The existing encounter/understanding fields are preserved. |
| Curated/example/unknown-origin turn | No personal-history contribution. Curated trails remain available and visibly labeled. |
| Catalog recommendation | Subject/format/source evidence, not a claim of personal engagement. |

`Holes.Origin` and `Nodes.Origin` support `user`, `curated`, `example`, and `legacy-unknown`. Each turn has its own origin. An interactive new branch explicitly records `user`; earlier curated nodes stay curated. API curators must send their origin when creating a trail; additional nodes inherit curated/example origin unless explicitly overridden. Merely navigating a curated trail cannot relabel it. Unknown legacy trails stay unknown instead of being assigned to the user.

Daily entries store version-2 structured rationale JSON: kind, summary, supporting item IDs/titles, interaction/concept/field matches, and the actual ranking contributions including selection-time diversity. The concise summary appears on cards, feature/Surprise cards and branch suggestions. **Why this?** expands source evidence and links; raw scores remain out of the normal interface. Stored evidence describes the time of selection, so a later unsave does not rewrite the historical explanation. Old entries retain their original data in exports, while the UI uses an honest legacy fallback instead of repeating an unsupported exploration claim.

## Concept and branch changes

`Concepts.cs` contains 28 small, inspectable concept definitions with phrase aliases, subject families and proposed adjacent subjects. Matching examines title, excerpt, allowlisted subject metadata and bounded available source text. Generic “workspace,” “model,” “information,” and software version terms do not become those specialized concepts. Generic “integrated information” needs consciousness context; a Younger Dryas match is labeled research rather than assuming an impact claim. Long-body matches require two non-overlapping mentions and receive less weight. A phrase match is evidence of a subject mention, not agreement, citation, factual truth or verified centrality.

The reader's **Concepts & tags** panel lets the user exclude an automatic concept or add known/custom tags. These overrides are stored separately from source text, survive refresh/identity reconciliation, and never change broad topic preferences. Custom shared tags work outside the initial vocabulary. The eight broad categories remain unchanged.

Related recommendations use specific shared concepts, neighboring concepts, family context or informative phrases, then at most two clearly labeled broad-topic fallbacks when fewer than four supported choices exist. Selection penalizes repeated concepts/directions/sources, near-duplicate titles and announcement clusters. A useful same-source continuation remains possible; no minimum count forces unrelated filler. The labels are descriptive, not mandatory slots, and a cross-disciplinary relationship can exist within a broad concept family.

`GET /api/items/{id}/related?holeId=…` excludes every item already on that trail, including ancestors and sibling branches. `includeVisited=true` exposes explicitly marked revisits. Bounded `visitedIds` supports unsaved paths; merged item aliases are resolved. The existing short reading-trail endpoint uses the same ranker and visited exclusions. Parent/sibling navigation, notes and saved positions retain their existing persistence behavior.

## Daily selection

Topic weights/pins remain strongest. Saved/liked item preferences can remove a negative daily format adjustment. Routine servicing, release-note and administrative-title patterns receive a small reduction; announcement phrases receive a smaller one. Available research abstracts and explanatory titles/descriptions receive modest positive contributions. These are format heuristics, not an objective measure of intellectual merit. No article is deleted or blocked from search because of its format or viewpoint.

Finite edition size, UTC daily persistence, source diversity, unknown-date handling, freshness controls, deterministic older-item rotation and configured serendipity remain. The frozen comparison keeps edition size 25, serendipity 0.15 and freshness 0.5. Both old and new selections have 22 primary-source/provider groups with at most two items in any group. All 25 new frozen daily explanations accurately name explicit interests; none claims personal exploration. This does not mean every card needs a different rationale when the actual evidence is the same.

The old frozen daily includes “.NET and .NET Framework October 2026 servicing releases updates” and “Google DeepMind and Singapore Government collaborate to build fine-tuned AI model for scam detection.” Neither appears in the new frozen daily. Maintenance items remain available in the catalog/search. Broad promotional titles such as “Welcome Beam…” and “Putting Earth AI to work…” still appear: the conservative pattern set deliberately does not pretend to detect every promotional article.

## Reproducible comparison

Before editing ranking, `scripts/audit-recommendations.py` captured the real export, related recommendations for all 276 records, the current daily edition and seven three-level walks. Each walk chooses the first unvisited recommendation; baseline output still records ancestors that the old API re-offered. The final algorithm receives that same visited set. No source refresh or AI call is part of this evaluation.

`tests/recommendations` replays the export with the exact timestamp `2026-10-18T04:09:47.5644749+00:00`, original topics, sources, settings, item state and nodes. The old scorer was extracted from commit `d849ff0fe207fb172db8afcc7c0785a48a8ef50f` and only renamed/fixed to that clock. Its ordering matched the captured live baseline for all 276 starts. The final live PostgreSQL related ordering and reasons matched the new frozen replay for all 276 starts and all seven visited-aware walks.

The seven seed IDs occur only in evaluation inputs. The production ranker has no item-ID or source-specific exceptions for these paths. The two known trail IDs in the upgrade are provenance repairs, not ranking rules.

### Consistent qualitative rubric

- **Strong:** a specific shared subject is directly supported by title/abstract/available text.
- **Adjacent:** a useful neighboring subject or broader explanation; an exploratory bridge, not a claim that both sources discuss the same problem.
- **Broad:** only a coarse umbrella relationship; useful possibilities but weak guidance.
- **Incidental:** shared words/branding do not explain a useful subject transition.

The following judgments assess the first three suggestions, not source truth or credibility. They are an engineering review, not a blinded human study or a numerical relevance score.

| Start | Before: first three | After: first three | Branch variety and remaining limitation |
|---|---|---|---|
| Global workspace | Strong / Incidental / Strong | Strong / Strong / Adjacent | Adds introspection alongside cognitive theory and contemplative models; still favors the available GWT papers. |
| Sumerian flood | Adjacent / Incidental / Broad | Adjacent / Adjacent / Adjacent | Replaces UFO/radar detours with geology and Younger Dryas research; collection lacks enough alternative flood-tradition material. |
| Project Blue Book | Broad / Adjacent / Broad | Broad / Adjacent / Broad | The FOIA direction is clearer, but recent UAP coverage and one source still dominate. |
| Remote Viewing | Broad / Broad / Broad | Adjacent / Adjacent / Adjacent | Psi experiments and altered states are more explicit options; no close protocol comparison is available from the sparse seed text. |
| Gemini robotics | Broad / Broad / Incidental | Strong / Adjacent / Strong | Technical robotics, planning and tactile learning replace repeated company announcements. |
| Biquad calculator | Adjacent / Broad / Strong | Strong / Adjacent / Adjacent | Filter tutorial, sampling and production offer distinct directions; some conceptual bridges remain broad. |
| Tactile robot learning | Adjacent / Strong / Broad | Strong / Strong / Strong | Keeps embodied/world-model work in view; all three are still a fairly concentrated research cluster. |

Across the seven three-level walks, the old API re-offered 17 already-visited candidates; the path-aware version re-offered zero. These are observed duplicate occurrences, not a percentage quality score. Intentional revisits are covered separately by HTTP and browser tests.

## Actual seven-path results

The tables below retain source titles and full item IDs. “Before” reasons are the old API's actual strings; it did not store numeric related-ranking evidence. Shared terms/phrases or broad topic overlap are therefore the recorded evidence, not retroactively invented concept rationales. After reasons and evidence come directly from the new output. Each three-step walk records the selected source at every level; full candidate arrays, scores and all-item comparisons remain in the private audit files.

### AI consciousness

Start: A global workspace in language models (`c25d45c7-d764-4162-a9f9-49218adc6f2c`).

| Rank | Before | After | After: recorded evidence |
|---|---|---|---|
| 1 | Content Without Access: A Cross-Family Fleet Measurement of Global Workspace Properties in Transformer Language Models (`ceec3831-0759-4a30-b43d-ceefed72774c`)<br>Shared phrase: global workspace; language models | Content Without Access: A Cross-Family Fleet Measurement of Global Workspace Properties in Transformer Language Models (`ceec3831-0759-4a30-b43d-ceefed72774c`)<br>Continue with Global Workspace Theory | starting-item: title match global workspace; candidate: title match global workspace |
| 2 | Unlocking Earth AI’s planetary geospatial foundation models for global public health (`c0e82840-fb90-4b9d-8192-3aab32185330`)<br>Shared terms: global, models | A Neuro-Functional Framework of Patanjali's Ashtanga Yoga: Mapping Ancient Mind Modulation Systems to Global Workspace Theory (`a19de1d3-3f44-46c5-a162-cc6271accd9e`)<br>Continue with Global Workspace Theory | starting-item: title match global workspace; candidate: title match global workspace |
| 3 | A Neuro-Functional Framework of Patanjali's Ashtanga Yoga: Mapping Ancient Mind Modulation Systems to Global Workspace Theory (`a19de1d3-3f44-46c5-a162-cc6271accd9e`)<br>Shared phrase: global workspace | Emergent introspective awareness in LLMs (`9a217cd6-e37a-4575-a04f-933a2fa4a050`)<br>Global Workspace Theory → AI consciousness | starting-item: title match global workspace; candidate: title match introspective awareness |

**Before: three selected steps**

1. Content Without Access: A Cross-Family Fleet Measurement of Global Workspace Properties in Transformer Language Models (`ceec3831-0759-4a30-b43d-ceefed72774c`). Reason: Shared phrase: global workspace; language models.
2. Psychedelics Restructure the Global Workspace of Consciousness (`d37da147-e756-44a2-b218-e3ac48f22898`). Reason: Shared phrase: global workspace.
3. A Neuro-Functional Framework of Patanjali's Ashtanga Yoga: Mapping Ancient Mind Modulation Systems to Global Workspace Theory (`a19de1d3-3f44-46c5-a162-cc6271accd9e`). Reason: Shared phrase: global workspace.

**After: three selected steps**

1. Content Without Access: A Cross-Family Fleet Measurement of Global Workspace Properties in Transformer Language Models (`ceec3831-0759-4a30-b43d-ceefed72774c`). Reason: Continue with Global Workspace Theory. Evidence: starting-item: title match global workspace; candidate: title match global workspace.
2. A Neuro-Functional Framework of Patanjali's Ashtanga Yoga: Mapping Ancient Mind Modulation Systems to Global Workspace Theory (`a19de1d3-3f44-46c5-a162-cc6271accd9e`). Reason: Continue with Global Workspace Theory. Evidence: starting-item: title match global workspace; candidate: title match global workspace.
3. Psychedelics Restructure the Global Workspace of Consciousness (`d37da147-e756-44a2-b218-e3ac48f22898`). Reason: Continue with Global Workspace Theory. Evidence: starting-item: title match global workspace; candidate: title match global workspace.

The old second result was an Earth AI/geospatial post selected for “global, models.” It drops from the first three. A GWT bridge to the Patanjali paper is supported by the actual title, while the introspection option is labeled an adjacent AI-consciousness subject. The three-level journey still stays within the GWT cluster.

### Flood traditions

Start: The flood story — Sumerian text translation (ETCSL 1.7.4) (`a76c12ee-7680-4e4e-b32d-3f91fafe9f0c`).

| Rank | Before | After | After: recorded evidence |
|---|---|---|---|
| 1 | The Late Pleistocene Catastrophe: A Cross-Disciplinary Framework Aligning Younger Dryas Impact Evidence, Postglacial Flood Geology, and Oral Tradition Methodology (`fe15f258-ef22-4ed1-a00c-41abe43ed46b`)<br>Shares a broad topic; an adjacent path to explore | The Late Pleistocene Catastrophe: A Cross-Disciplinary Framework Aligning Younger Dryas Impact Evidence, Postglacial Flood Geology, and Oral Tradition Methodology (`fe15f258-ef22-4ed1-a00c-41abe43ed46b`)<br>Flood traditions → Flood geology and paleoclimate | starting-item: title match flood story; candidate: title match postglacial flood |
| 2 | The Black Vault Launches Searchable UFO Files Release Archive (`10bae5de-eb44-46bf-b0cd-8373e1b7ebe5`)<br>Shares a broad topic; an adjacent path to explore | Large Pt anomaly in the Greenland ice core points to a cataclysm at the onset of Younger Dryas (`41fbf555-6f34-4998-b103-220c3f8cde58`)<br>Flood traditions → Younger Dryas research | starting-item: title match flood story; candidate: title match younger dryas |
| 3 | Climate Change Is Altering the Atmosphere in Ways That Could Make Military Radar See Farther—or Go Blind (`160e8818-d738-42e9-a8b7-3444a0845c8d`)<br>Shares a broad topic; an adjacent path to explore | Younger Dryas Impact Hypothesis (`55121754-f1b9-4782-a090-7375992657b5`)<br>Flood traditions → Younger Dryas research | starting-item: title match flood story; candidate: title match younger dryas |

**Before: three selected steps**

1. The Late Pleistocene Catastrophe: A Cross-Disciplinary Framework Aligning Younger Dryas Impact Evidence, Postglacial Flood Geology, and Oral Tradition Methodology (`fe15f258-ef22-4ed1-a00c-41abe43ed46b`). Reason: Shares a broad topic; an adjacent path to explore.
2. Identification of a New England Bolide Impact Site: A Geologic Reckoning with the Ground-Zero for the Younger Dryas Impact Event (`a4f94bd1-1095-42f6-bebf-bdb448618b75`). Reason: Shared phrase: dryas impact; younger dryas.
3. A spatial gradient without a radial signature: a county-level test of the Younger Dryas impact hypothesis demographic prediction in PIDBA (analysis code and data) (`58e8a449-bc77-47df-889e-554163c21608`). Reason: Shared phrase: dryas impact; younger dryas.

**After: three selected steps**

1. The Late Pleistocene Catastrophe: A Cross-Disciplinary Framework Aligning Younger Dryas Impact Evidence, Postglacial Flood Geology, and Oral Tradition Methodology (`fe15f258-ef22-4ed1-a00c-41abe43ed46b`). Reason: Flood traditions → Flood geology and paleoclimate. Evidence: starting-item: title match flood story; candidate: title match postglacial flood.
2. Identification of a New England Bolide Impact Site: A Geologic Reckoning with the Ground-Zero for the Younger Dryas Impact Event (`a4f94bd1-1095-42f6-bebf-bdb448618b75`). Reason: Continue with Younger Dryas research. Evidence: starting-item: title match younger dryas; candidate: title match younger dryas.
3. Younger Dryas Impact Hypothesis (`55121754-f1b9-4782-a090-7375992657b5`). Reason: Continue with Younger Dryas research. Evidence: starting-item: title match younger dryas; candidate: title match younger dryas.

The bridge is between a flood narrative and papers about flood geology/Younger Dryas. It does not establish that the ancient text describes a particular impact or event. The catalog currently funnels this path toward impact research; a varied comparative-mythology journey cannot be manufactured from absent records.

### UFO history

Start: Project BLUE BOOK — Unidentified Flying Objects (`b42751cd-a1e0-45e6-933c-e9c1ef8d111e`).

| Rank | Before | After | After: recorded evidence |
|---|---|---|---|
| 1 | Israeli Military Continues the Search for an Unidentified Aerial Object Detected Near Lebanese Border (`63fce5c2-c904-4f46-a1e6-b6845c4c2a96`)<br>Shared terms: incident, force, unidentified | Department of War Cites National Security to Hide Core UAP Evaluation Details (`40e7fefb-8bd2-4711-b295-fbe3e79cc755`)<br>Continue with UFO and UAP reports | starting-item: excerpt match ufos; candidate: title match uap |
| 2 | Pentagon Says It Has No Record of “Immaculate Constellation” — NSA Denies Appeal and Won’t Say Whether It Does (`1c891484-11ca-42e5-ba80-d20fc734b0a6`)<br>Shared terms: national, records | New Emails Reveal Pentagon Effort to Align Messaging on AATIP and Luis Elizondo (`402f9651-477d-4c22-ba6d-445b5203ed6d`)<br>Historical UFO investigations → UAP documents and FOIA | starting-item: title match unidentified flying objects; candidate: title match aatip |
| 3 | What Timothy Gallaudet’s NOAA Emails Reveal About His Interest in UAPs (`7457fbac-7063-479d-8e9d-9d1f3840745b`)<br>Shared terms: national, records | Israeli Military Continues the Search for an Unidentified Aerial Object Detected Near Lebanese Border (`63fce5c2-c904-4f46-a1e6-b6845c4c2a96`)<br>Continue with UFO and UAP reports | starting-item: excerpt match ufos; candidate: title match unidentified aerial |

**Before: three selected steps**

1. Israeli Military Continues the Search for an Unidentified Aerial Object Detected Near Lebanese Border (`63fce5c2-c904-4f46-a1e6-b6845c4c2a96`). Reason: Shared terms: incident, force, unidentified.
2. Navy Releases Massive Cache of Scott Bray UAP Emails After Four Years; More Than 300 Pages Withheld in Full (`aa66e0c7-0c1d-4bd5-9572-ee9263d74b9a`). Reason: Shared phrase: unidentified aerial.
3. New Emails Reveal Pentagon Effort to Align Messaging on AATIP and Luis Elizondo (`402f9651-477d-4c22-ba6d-445b5203ed6d`). Reason: Shared terms: emails, pentagon, director.

**After: three selected steps**

1. Department of War Cites National Security to Hide Core UAP Evaluation Details (`40e7fefb-8bd2-4711-b295-fbe3e79cc755`). Reason: Continue with UFO and UAP reports. Evidence: starting-item: excerpt match ufos; candidate: title match uap.
2. Department of War Issues New UAP Disclosure Waiver; But Similar Protections Already Existed (`bb7369b2-9db3-4939-8e41-34089b84a17d`). Reason: Continue with UFO and UAP reports. Evidence: starting-item: title match uap; candidate: title match uap.
3. Newly Released Documents Show UAP “Space Tiger Team” Built Around Space and Transmedium Cases (`c446e391-ab41-450a-a4fe-71e58e41303c`). Reason: Continue with UFO and UAP reports. Evidence: starting-item: title match uap; candidate: title match uap.

This is the weakest improvement among the seven. The AATIP/document connection is explicit and ancestors disappear, but general UAP reports still receive close-continuation labels and the walk remains concentrated in The Black Vault. Historical investigation comparisons would need richer existing metadata/tags or more relevant content in a later, separately requested curation pass.

### Remote viewing

Start: Remote Viewing — Psi Encyclopedia (`0023d292-5d70-4c67-a1a1-81364efd4d6f`).

| Rank | Before | After | After: recorded evidence |
|---|---|---|---|
| 1 | Reincarnation: An Introduction — Psi Encyclopedia (`e3ad2c7f-1eef-4d83-88e5-bd6847612a45`)<br>Shares a broad topic; an adjacent path to explore | Recent Research on Precognitive Lucid Dreaming (`42ffd153-a021-4197-bf06-6515bf5fbdb8`)<br>Remote viewing → Psi experiments | starting-item: title match remote viewing; candidate: title match precognitive |
| 2 | Near-Death Experiences (Overview) — Psi Encyclopedia (`deff714c-e3d8-4d10-b8b1-b9aaff069af3`)<br>Shares a broad topic; an adjacent path to explore | A Neuro-Functional Framework of Patanjali's Ashtanga Yoga: Mapping Ancient Mind Modulation Systems to Global Workspace Theory (`a19de1d3-3f44-46c5-a162-cc6271accd9e`)<br>Remote viewing → Altered states of consciousness | starting-item: title match remote viewing; candidate: title match ashtanga yoga |
| 3 | Investigating Remote Electrogastrogram Modulation Via Emotional and Eating Stimuli: A Sham-Controlled Pilot Study (`0128ef50-2e6a-480e-9132-fd63ad24c274`)<br>Shares a broad topic; an adjacent path to explore | Consciousness Science Goes Mainstream: Review of “The Secret of Secrets,” “The Telepathy Tapes,” and “Beyond the Brain” (`ef2a7a3d-065e-46dd-877c-f585e1e7abe2`)<br>Remote viewing → Psi experiments | starting-item: title match remote viewing; candidate: title match telepathy |

**Before: three selected steps**

1. Reincarnation: An Introduction — Psi Encyclopedia (`e3ad2c7f-1eef-4d83-88e5-bd6847612a45`). Reason: Shares a broad topic; an adjacent path to explore.
2. Dorothy Eady/Omm Sety (reincarnation case) (`1da7a72f-0d33-4d38-b995-7daf507bc435`). Reason: Shares a broad topic; an adjacent path to explore.
3. Exploring the History of Reincarnation Beliefs (`4889c078-9d10-47cf-b56a-d606c2264ebb`). Reason: Shares a broad topic; an adjacent path to explore.

**After: three selected steps**

1. Recent Research on Precognitive Lucid Dreaming (`42ffd153-a021-4197-bf06-6515bf5fbdb8`). Reason: Remote viewing → Psi experiments. Evidence: starting-item: title match remote viewing; candidate: title match precognitive.
2. Consciousness Science Goes Mainstream: Review of “The Secret of Secrets,” “The Telepathy Tapes,” and “Beyond the Brain” (`ef2a7a3d-065e-46dd-877c-f585e1e7abe2`). Reason: Continue with Psi experiments. Evidence: starting-item: title match precognitive; candidate: title match telepathy.
3. The Near-Death Experience, an Instance of Higher-Dimensional Phenomenal Consciousness? (`ead42f4e-d794-4b38-a3e8-751ebfc6f457`). Reason: Psi experiments → Near-death experiences. Evidence: starting-item: title match telepathy; candidate: title match near death experience.

The encyclopedia seed has little locally stored text. The new choices are declared neighboring subjects: psi experiments, altered states and consciousness research. These are more legible branches than an unexplained jump to reincarnation, but they are not evidence of a shared remote-viewing method. Human judgment is especially valuable here.

### Robotics announcement

Start: Gemini 4 Argon for Robotics (`0ee48966-38f2-4104-8005-4d6f1426072b`).

| Rank | Before | After | After: recorded evidence |
|---|---|---|---|
| 1 | Developing Gemini 4 Argon responsibly and safely (`16dc1036-18d6-4be5-bc06-6af9827a4a20`)<br>Shared phrase: gemini argon | From Intelligent Control to Integrated Autonomy: Reflections on Advanced Intelligent Robotics and Autonomous Control Systems (`7d0d0e00-09cf-452b-a29a-b1e00016fac8`)<br>Continue with Robotics | starting-item: title match robotics; candidate: title match robotics |
| 2 | Gemini 4 Argon: our next era of frontier intelligence (`a96e0386-696b-418b-ac0b-1b55c857f2b8`)<br>Shared phrase: gemini argon | Gradient-based Planning for World Models at Longer Horizons (`afebea1d-4afd-4c92-a92c-603d779f96a8`)<br>Robotics → World models | starting-item: title match robotics; candidate: title match world models |
| 3 | llm-gemini 0.35 (`360e4302-6b1e-40a6-91de-03dc65cade0b`)<br>Shared phrase: gemini argon | Tactile-Guided Robot Learning: From Representation Learning to Foundation Models (`37b2a02a-4e9e-42b5-b645-edcb78c96870`)<br>Continue with Robotics | starting-item: title match robotics; candidate: title match robot |

**Before: three selected steps**

1. Developing Gemini 4 Argon responsibly and safely (`16dc1036-18d6-4be5-bc06-6af9827a4a20`). Reason: Shared phrase: gemini argon.
2. Gemini 4 Argon: our next era of frontier intelligence (`a96e0386-696b-418b-ac0b-1b55c857f2b8`). Reason: Shared phrase: gemini argon.
3. llm-gemini 0.35 (`360e4302-6b1e-40a6-91de-03dc65cade0b`). Reason: Shared phrase: gemini argon.

**After: three selected steps**

1. From Intelligent Control to Integrated Autonomy: Reflections on Advanced Intelligent Robotics and Autonomous Control Systems (`7d0d0e00-09cf-452b-a29a-b1e00016fac8`). Reason: Continue with Robotics. Evidence: starting-item: title match robotics; candidate: title match robotics.
2. Tactile-Guided Robot Learning: From Representation Learning to Foundation Models (`37b2a02a-4e9e-42b5-b645-edcb78c96870`). Reason: Continue with Embodied learning. Evidence: starting-item: excerpt match embodied intelligence; candidate: title match robot learning.
3. Thinker-WM: A Unified World Model with Shared Multimodal Representations for Embodied Intelligence (`c4a2aef9-0fc3-44d5-b325-ad0c58cd30d2`). Reason: Continue with Embodied learning. Evidence: starting-item: title match robot learning; candidate: title match embodied intelligence.

The shared-brand chain is gone from the first three levels. A broad robotics reflection ranks first, followed by tactile learning and embodied intelligence. The first reflection is an editorial with an abstract, so the format heuristic must not be read as a guarantee of original research.

### Audio DSP

Start: Biquad calculator v3 (`2d613310-204d-4f30-a627-b7e55f77738b`).

| Rank | Before | After | After: recorded evidence |
|---|---|---|---|
| 1 | Inter-sample peaks (`d821c0e8-b481-4561-a817-8f951d050917`)<br>Shared terms: values, peak, signal | How To Use MNTRA FLTRS (`034c8123-8716-4134-bb7b-0600cdad6dd3`)<br>Continue with Digital filters | starting-item: excerpt match frequency response; candidate: excerpt match highpass |
| 2 | AM widget (`931aad6e-e71e-4c60-b1b6-592cc87df894`)<br>Shared terms: yes, adding, rate | Inter-sample peaks (`d821c0e8-b481-4561-a817-8f951d050917`)<br>Digital filters → Audio sampling and peaks | starting-item: excerpt match frequency response; candidate: title match inter sample peaks |
| 3 | How To Use MNTRA FLTRS (`034c8123-8716-4134-bb7b-0600cdad6dd3`)<br>Shared terms: bandpass, highpass, lowpass | Tips for Riddim Sound Design & Introduction to Vital w/ INFEKT (`7d1d94d7-a614-4432-84a5-4d40960e30e4`)<br>Digital filters → Sound design | starting-item: excerpt match frequency response; candidate: title match sound design |

**Before: three selected steps**

1. Inter-sample peaks (`d821c0e8-b481-4561-a817-8f951d050917`). Reason: Shared terms: values, peak, signal.
2. AM widget (`931aad6e-e71e-4c60-b1b6-592cc87df894`). Reason: Shared terms: signal, amplitude, output.
3. Amplitude Modulation Principles and Interactive Widget video (`8baf6ad6-f66a-44c9-a1f2-8fb5b04c0991`). Reason: Shared terms: widget, amplitude, modulation.

**After: three selected steps**

1. How To Use MNTRA FLTRS (`034c8123-8716-4134-bb7b-0600cdad6dd3`). Reason: Continue with Digital filters. Evidence: starting-item: excerpt match frequency response; candidate: excerpt match highpass.
2. Inter-sample peaks (`d821c0e8-b481-4561-a817-8f951d050917`). Reason: Digital filters → Audio sampling and peaks. Evidence: starting-item: excerpt match highpass; candidate: title match inter sample peaks.
3. Time resolution in digital audio (`6da5574a-2947-4e1e-80df-778cfa4474db`). Reason: Continue with Audio sampling and peaks. Evidence: starting-item: title match inter sample peaks; candidate: excerpt match sample rates.

The new first hop connects digital-filter terminology in the calculator text to a practical filter tutorial. Sampling and riddim/sound design are separate neighboring directions. The actual walk then reaches inter-sample peaks and time resolution; it does not assert the INFEKT video explains biquad coefficients.

### Robot learning

Start: Tactile-Guided Robot Learning: From Representation Learning to Foundation Models (`37b2a02a-4e9e-42b5-b645-edcb78c96870`).

| Rank | Before | After | After: recorded evidence |
|---|---|---|---|
| 1 | What exactly does word2vec learn? (`2065ff20-4b35-4405-b9f4-f32dee7f4bbb`)<br>Shared phrase: representation learning | Thinker-WM: A Unified World Model with Shared Multimodal Representations for Embodied Intelligence (`c4a2aef9-0fc3-44d5-b325-ad0c58cd30d2`)<br>Continue with Embodied learning | starting-item: title match robot learning; candidate: title match embodied intelligence |
| 2 | Thinker-WM: A Unified World Model with Shared Multimodal Representations for Embodied Intelligence (`c4a2aef9-0fc3-44d5-b325-ad0c58cd30d2`)<br>Shared terms: policy, representation, examine | MARS: Systematic Multi-Level Acceleration for World Action Models (`f647823d-47a2-4409-9ac2-8db3b2b99c5c`)<br>Continue with World models | starting-item: excerpt match world model; candidate: title match world action models |
| 3 | Toward provably private learning from federated data (`2448ac94-0e46-4ec7-94d4-af45ab1c0009`)<br>Shared terms: learning, data | Gradient-based Planning for World Models at Longer Horizons (`afebea1d-4afd-4c92-a92c-603d779f96a8`)<br>Continue with World models | starting-item: excerpt match world model; candidate: title match world models |

**Before: three selected steps**

1. What exactly does word2vec learn? (`2065ff20-4b35-4405-b9f4-f32dee7f4bbb`). Reason: Shared phrase: representation learning.
2. Gradient-based Planning for World Models at Longer Horizons (`afebea1d-4afd-4c92-a92c-603d779f96a8`). Reason: Shared terms: latent, learned, gradient.
3. Thinker-WM: A Unified World Model with Shared Multimodal Representations for Embodied Intelligence (`c4a2aef9-0fc3-44d5-b325-ad0c58cd30d2`). Reason: Shared phrase: world models.

**After: three selected steps**

1. Thinker-WM: A Unified World Model with Shared Multimodal Representations for Embodied Intelligence (`c4a2aef9-0fc3-44d5-b325-ad0c58cd30d2`). Reason: Continue with Embodied learning. Evidence: starting-item: title match robot learning; candidate: title match embodied intelligence.
2. MARS: Systematic Multi-Level Acceleration for World Action Models (`f647823d-47a2-4409-9ac2-8db3b2b99c5c`). Reason: Continue with World models. Evidence: starting-item: title match world model; candidate: title match world action models.
3. Agentic Embodied AI for Human–Robot Collaboration: Architectures, Technologies, and Future Directions (`9fc84f37-d2ae-419d-b565-5456686b003f`). Reason: Continue with Embodied learning. Evidence: starting-item: excerpt match embodied intelligence; candidate: title match embodied ai.

The word2vec detour was not inherently irrelevant—representation learning is a real bridge—but it was preferred over embodied context using generic words. The new first three share embodied/world-model concepts from the available titles and abstracts. Same-topic clustering remains a limitation.

## Useful bridges and limits

The GWT-to-contemplative-model and remote-viewing-to-altered-state suggestions connect named subjects across usual reading categories. Flood traditions can branch toward geological hypotheses without claiming those hypotheses explain the text. Digital filter design can reach sound-design practice rather than another calculator. These are invitations grounded in existing records, not fabricated citations or an assertion that the sources agree.

The hand-maintained vocabulary will miss concepts outside its 28 definitions, including paraphrases and non-English material. Precise matching intentionally leaves “Locality of integrated information” untagged when no consciousness context is available. Custom tags and broad labeled fallbacks provide usable paths without inventing specificity. A repeated body mention can still be tangential; those results are weakened and labeled text connections. The fourth robotics suggestion, a biological-naturalism essay mentioning robots, is one such weaker bridge.

Direction/source penalties encourage variety but cannot create missing subjects or overcome a very concentrated collection. UAP paths retain substantial same-source clustering. Broad promotional titles and research editorials can evade format patterns. Reader preferences, actual feedback, and a human assessment of whether these paths feel interesting remain better evidence than a heuristic score. No credibility, ideology, agreement or “approved source” weighting was added.

## Migration and personal-data preservation

The additive EF migration `20261018041856_RecommendationEvidence` adds item tag/exclusion arrays, hole/node origins, an origin note and daily rationale JSON. Existing rows default to `legacy-unknown`; new model objects default to `user`. SQLite receives equivalent additive columns. The idempotent `recommendation-provenance-v1` backfill changes only the two audited legacy trail origins and original nodes created no later than `2026-10-18T02:52:05Z`. Later/unknown nodes are not assumed to be curated or user-initiated.

The current legacy edition was archived by changing only its day key to `2026-10-18:before-recommendation-evidence:dcd5c258-1ec2-4f5c-b9f8-3acd012fb835`. All its original entries, order, reasons and IDs remain. A normal Discover request created one new 25-item edition with recorded evidence. This is a one-time upgrade exception to edition stability; subsequent reloads/restarts retain the selected order and rationales. No history, saved state, source, item or trail was reset.

`scripts/verify-recommendation-preservation.py` compared every old exported row/field, allowing only that documented edition-key change. It reported no unexpected differences:

| Data | Before | After |
|---|---:|---:|
| Items | 276 | 276 |
| Topics / configured sources | 8 / 35 | 8 / 35 |
| Item identities / source memberships / topic evidence rows | 628 / 259 / 2,208 | 628 / 259 / 2,208 |
| Personal item-state rows | 7 | 7 |
| Rabbit holes / nodes | 2 / 10 | 2 / 10 |
| Notes / documents / sections / practice prompts / attempts | 0 | 0 |
| Editions / entries | 2 / 28 | 3 / 53 |

Every original item field, preference, state value, parent link, node ID and current-position pointer matched. Both original trails and all ten original nodes are now labeled curated. Production had no notes/documents to exercise; persistence of real note, saved/read/progress and branching mutations was instead checked through HTTP against a disposable copy on the **actual PostgreSQL server**, plus isolated browser fixtures. No test fixture rows were inserted into production.

A pre-change database/uploads backup is at `backups/20261018-000906`. Export version 3 includes the new fields. Downgrade intentionally requires restoring a matching database/uploads backup rather than dropping provenance/evidence columns. Normal backup/restore commands are in the README.

## Verification

| Actual command/check | Result |
|---|---|
| `dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release --no-restore` | 159 passed; zero failed/skipped. |
| `dotnet format apps/api/RabbitHole.Api.csproj --verify-no-changes --no-restore` | Passed. |
| `dotnet ef migrations has-pending-model-changes --project apps/api` | No pending model changes. CLI 10.0.12 reports that runtime 10.0.13 is newer. |
| `npm --prefix apps/web run lint` and `npm --prefix apps/web run typecheck` | Passed. |
| `npm --prefix apps/web run build` | Passed; final Docker build also compiled all production routes successfully. |
| `npm run test:e2e` | 15 passed in 42.2 seconds, including provenance, tags, explanations, revisits, persistence, no-AI behavior and 390px navigation. The focused recommendation test also passed after the final mobile branch-width adjustment. |
| `python -m unittest discover -s tests/scripts -v` | Seven source/reading-pack tests passed. |
| `docker compose up --build -d --wait` | API/web rebuilt, migration applied; all four services running, API/web/database health checks passing and gateway serving HTTP successfully. Only gateway published on loopback. |
| `./scripts/check-recommendations-postgres.ps1` | Two real HTTP tests passed on a fresh `pg_dump` copy of the production database in the actual PostgreSQL 17 instance; disposable database removed. |
| Frozen old/new evaluator and final live audit | 276 records audited; same fingerprint; all related order/reason comparisons and seven paths matched. |
| Pre/post upgrade export comparison | Passed, all original state preserved; only the documented edition-key representation changed. |
| `docker compose stop gateway web api db`, then `docker compose up -d --wait`, and exact export comparison with `--restart` | Passed. Every exported persistent row/field and all row counts, including edition evidence and current trail positions, were unchanged. |
| Production desktop/mobile visual checks | Eight page/viewport checks passed at 1440px and 390px, with no horizontal overflow, JavaScript errors or failed HTTP responses. Screenshots inspected for Discover, trails, branching and reader recommendations. |
| Production database and runtime inspection | 276 items; zero test-fixture titles or duplicate canonical-URL groups; two curated holes and ten curated nodes; latest migration present; zero application failure-pattern matches in recent logs. |

The backend tests cover unknown/curated/example origins, real action-specific explanations, temporary user turns, ambiguous terms, metadata/tag overrides, cross-topic/source variety, announcement chains, sparse results, visited/revisit handling and stable daily selection. Existing persistence/security/provider-failure tests continue to pass. The isolated PostgreSQL HTTP suite disables ingestion and AI and verifies its database name before writing fixtures.

Manual browser inspection expanded actual Discover/reader explanations, inspected the saved trail map/current position, and confirmed that enabling revisits changes the label to **Branch again** with **Already in this trail**. Reader visual checks used a temporary loopback-only read-through proxy that suppressed the reader's passive `opened` PATCH and rejected other writes, so QA did not become personal reading history. All displayed content and recommendations came from the production application. Write/persistence behavior was tested separately through the actual PostgreSQL clone and isolated E2E host. The temporary proxy was removed after inspection.

An intermediate mobile regression in Settings was caught by Playwright: a global action-row rule prevented wrapping. That rule was removed and the entire 15-test suite passed. A sandbox-only frontend build permission error was retried successfully with the normal local runtime. The offline audit printed NU1900 when the sandbox could not contact NuGet vulnerability metadata; evaluation completed and the Docker test restore succeeded. No test result is inferred from an unavailable runtime.

No new provider smoke/import was needed: this pass makes no provider-interface changes and deliberately holds the catalog fixed. Existing provider behavior was exercised by the complete fixture suite. Live playback/transcript access and AI compatibility were not newly asserted. Zero AI credentials are required and no live model call was made.

## Reproduction and private audit artifacts

The full export/score files are intentionally ignored by Git because they contain private state. On this installation they remain under `data/recommendation-quality/`: `before/snapshot.json`, `before/audit.json`, `before-offline.json`, `after-offline.json`, `final-live/`, `preservation.json`, `restart-preservation.json`, and `visual/`. The original database/uploads backup is separate from these inspection files.

```powershell
# Read-only audit of the running app; does not open sources or mark them read.
python scripts/audit-recommendations.py --output data/recommendation-quality/current --path-aware

# Replay the frozen original catalog with the final ranker and fixed clock.
dotnet run --project tests/recommendations --no-restore -- data/recommendation-quality/before/snapshot.json data/recommendation-quality/before/audit.json data/recommendation-quality/after-offline.json

# The saved LegacyRanking.cs is the baseline scorer renamed to avoid a collision,
# with DateTimeOffset.UtcNow replaced by AuditClock.Now (no ranking-rule edits).
dotnet run --project tests/recommendations --no-restore '-p:LegacyRankingFile=C:/Users/The Enforcer/Projects/RabbitHole/data/recommendation-quality/LegacyRanking.cs' -- data/recommendation-quality/before/snapshot.json data/recommendation-quality/before/audit.json data/recommendation-quality/before-offline.json

# Compare final live catalog/settings with the original export, then every old field.
python scripts/audit-recommendations.py --output data/recommendation-quality/current --snapshot data/recommendation-quality/before/snapshot.json --path-aware
python scripts/verify-recommendation-preservation.py data/recommendation-quality/before/snapshot.json data/recommendation-quality/current/snapshot.json

# Full application checks, browser suite and actual PostgreSQL verification.
./scripts/check.ps1
npm run test:e2e
./scripts/check-recommendations-postgres.ps1
```

The snapshot helper calls the existing export endpoint and reads the daily edition; if no current edition exists, its ordinary GET can create that edition. It does not refresh providers, deliberately rebuild an existing edition, create trails or change item states. Future changing catalogs should be evaluated from a new frozen export rather than compared to these historical paths.
