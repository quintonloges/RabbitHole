# Blackjack Lab

`/blackjack` is an anonymous, self-contained learning destination linked from the public and private navigation. It has no API requests, account, database tables, AI calls, tracking, or connection to content recommendations. The existing public shell supplies navigation and theme; the lab owns its scoped styles.

## Experience

- **Learn:** seven open-access lessons, 33 short sections, card-value and outcome exercises, strategy decisions, and replayable demonstrations of all six actions (including insurance). Completion requires attempting each section, but never locks navigation.
- **Practice:** 660 distinct scenarios in a shuffled bag, with all hands, hard totals, soft totals, splitting, doubling, and surrender categories. Includes hands after hitting and splitting, plus counterexamples where a drill's named action is not best. Each scenario appears once before the bag refills. Correct/attempted counts reset with the session or category.
- **Play:** a shuffled six-deck shoe, virtual wagers, full insurance/peek/player/dealer/settlement phases, up to four hands, and optional coaching enabled initially. The player’s chosen action always executes. Cards arrive in order, the hole card flips before dealer draws, and results follow a short settling pause. Reduced-motion preferences skip motion and presentation delays. Switching modes retains the current session.
- **Reference:** complete hard/soft/pair charts render the exact typed strategy tables used by the evaluator. Rules, fallbacks, and source links are available in the interface.

Browser storage under `rabbithole-blackjack-v2` stores stable lesson IDs, the last lesson, and the coaching preference. Each completion and preference has its own key, so concurrent tabs cannot overwrite unrelated progress. Storage events synchronize open tabs, including resets. Numeric v1 progress migrates once using an explicit mapping of the original lesson order; lesson counts come from the current catalog. Missing, malformed, or blocked storage never prevents use. The footer resets these values and removes legacy progress; Play separately resets the table and virtual chips to $500 between rounds. Scores, hands, and chips are intentionally not persisted across reloads. No new environment variables or dependencies are required.

## Rules and references

Verified on 29 October 2026 against Michael Shackleford’s Wizard of Odds:

- [Blackjack rules](https://wizardofodds.com/games/blackjack/basics/): scoring, dealing, actions, and settlement.
- [Four-to-eight-deck strategy](https://wizardofodds.com/games/blackjack/strategy/4-decks/): the **S17** chart and its written surrender, split, double, hit/stand rules.
- [Strategy calculator](https://wizardofodds.com/games/blackjack/strategy/calculator/): the matching deck/S17/DAS/surrender/peek dimensions and insurance guidance.
- [Surrender](https://wizardofodds.com/games/blackjack/surrender/): four-or-more-deck **total-dependent late surrender**, distinct from composition-dependent exceptions.

The fixed rules are six standard decks; S17; American hole card and peek; 3:2 natural payouts; 2:1 insurance on a half-size bet; any initial two cards may double; DAS; late surrender on the original two cards only; four hands maximum; no resplitting aces; one card per split Ace with no hit or double; and no natural after a split. Equal-value 10/J/Q/K cards may split. An initial wager is $5–$500 in $1 increments. Double/split wagers may take the round total above the opening table maximum.

This is conventional **total-dependent basic strategy**, including the pair exception for 8s. It deliberately does not implement card counting, composition-dependent exceptions, or other casino variants. In particular, hard 11 hits against Ace; soft 18 doubles against 3–6 and stands if the double is unavailable; hard 16 surrenders against 9/10/A except when a pair of 8s can split. A player unable to fund a split uses the total, including surrender if legal. The coach receives only the visible hand, upcard, and legal actions.

## Code boundaries

`apps/web/lib/blackjack/` contains independent TypeScript modules:

| Module | Responsibility |
|---|---|
| `cards.ts` | Physical card identities, six-deck shoe, Fisher–Yates shuffle, scoring, natural detection |
| `rules.ts` | Constants actually consumed by the engine, documented fixed invariants, wager limits, integer-cent formatting; not a configurable casino rules interface |
| `engine.ts` | Immutable transitions, legal moves, debit/credit ledger, dealer steps |
| `table-motion.ts` | Presentation frames for completed engine transitions: card visibility, hole-card reveal, displayed balance and short waits |
| `strategy.ts` | Typed hard/soft/pair matrices, legal-action fallbacks, recommendation |
| `coaching.ts` | Authored explanations and feedback captured before the action |
| `practice.ts` | Valid scenario enumeration and shuffled bags without replacement |
| `lessons.ts` | Authored teaching sections and explicit deterministic lesson examples |
| `progress.ts` | Stable lesson IDs, legacy migration, defensive parsing, independent field updates and reset |

React components live in `apps/web/components/blackjack/`. `app/(public)/blackjack/page.tsx` provides the route and metadata, importing the feature-scoped `blackjack.css`. No blackjack rules are implemented in React; lesson demonstrations also execute the engine. The chart renders table cells rather than a separately maintained image. The API and its authentication policy are unchanged.

All money is integer cents. Valid opening bets are whole dollars, so halves and 3:2 payouts remain exact. Stake placement debits immediately; settlement credits stake plus profit once. Insurance is settled independently at the peek. A winning ordinary $10 hand credits $20; a $10 natural credits $25; surrender credits $5. Busts are final before dealer play. Waiting split hands receive their second card only when their turn starts; split aces receive both forced cards and end their turns.

The completed-round breakdown shows each main wager and any insurance separately, with bet, returned, and net columns plus combined totals from the engine. For example, a losing $10 main bet and winning $5 insurance return $15 overall for a $0 net. Dealer blackjack ends the round at the peek whether insurance is accepted or declined; another natural pushes the main bet. If the dealer has no blackjack, insurance loses and the main hand proceeds normally. Play labels revealed naturals as blackjack and explains immediate round endings.

The shoe is retained between rounds and replaced before a new round when fewer than 104 cards remain. It never reshuffles during a round. Engine tests inject ordered full shoes; Play always uses a fresh random shuffle on first deal and at the threshold. Exhaustion of a malformed injected shoe fails atomically instead of generating an extra card.

### Table pacing

The opening four cards arrive 160 ms apart with a 280 ms slide, completing in about 0.8 seconds. Hits, doubles and split draws take about 320 ms per new card; splitting first gives the new hands 200 ms to separate. The dealer’s hole card flips over 360 ms before further draws, and settlement waits another 240 ms before showing returns and the result panel. Chips and feedback use small, brief transitions; shared card motion also applies to lessons and practice.

The engine still executes each move atomically. Presentation frames reveal only the cards already drawn by that transition, in physical shoe order. Undealt slots reserve space but expose neither card names nor future totals. Bets debit visually at once; returns and final outcome labels wait until the sequence ends. Action controls are disabled during motion, with a synchronous guard against duplicate clicks before React renders. Timers clean up on reset/unmount, and switching modes preserves the ongoing round. Changing the system’s reduced-motion preference during a sequence immediately completes its presentation; future moves have no animation waits. No timing state is persisted.

After a focused action finishes, focus returns to that action when still available, or moves to the next decision (insurance, player action, or Deal). This also covers transitions with reduced motion. Switching modes or deliberately focusing another control during playback takes precedence; the table does not steal focus back.

## Run and verify

```powershell
./scripts/dev.ps1 start
# Open http://localhost:47832/blackjack
npm run test:blackjack
./scripts/check.ps1
npm run test:e2e
```

`test:blackjack` uses the repository's existing Playwright runner on loopback port 3002, starting **only Next.js**. No backend or provider connectivity is required. The engine/strategy specs are ordinary TypeScript tests in the same runner. They also run under the standard full-app `test:e2e` configuration, so CI automatically includes them.

Coverage includes every defined strategy cell against a separately authored oracle derived from the reference's written rules; available-action fallback combinations; deterministic engine sequences; 500 varied rounds checking the ledger and physical cards; every lesson exercise; browser lesson navigation/persistence/reset; practice feedback/counts; hidden cards and pre-action coaching; split sequencing; insurance; invalid wagers; keyboard mode navigation; 320–1440px layouts; light/dark presentation; denied storage; and continued play after browser networking is disabled. Clock-controlled browser checks cover staged dealing/totals, duplicate clicks, dealer reveal before payouts, natural settlement, split-Ace draw order, mode switching during motion, and reduced motion toggled mid-deal. Screenshots are saved under ignored `test-results/` for visual inspection. See `acceptance-report.md` for actual run results.

Initial page loading still requires the web app; this is not an installable offline PWA. Once loaded, the lab does not require a live API or network. Virtual chips have no monetary value.
