import { HARD, SOFT, PAIRS, UPCARDS, CELL_LABELS, type StrategyTable } from "@/lib/blackjack/strategy";
import { RULE_SUMMARY, RULESET_NAME, RULESET_NOTE, RULESET_SUMMARY } from "@/lib/blackjack/rules";

function ChartTable({ title, table, kind }: { title: string; table: StrategyTable; kind: "hard" | "soft" | "pair" }) {
  return <div className="bj-chart-scroll" tabIndex={0} role="region" aria-label={`${title} strategy table; scroll horizontally on small screens`}>
    <table className="bj-chart"><caption>{title}</caption><thead><tr><th scope="col">Your hand</th>{UPCARDS.map(up => <th key={up} scope="col">{up === 11 ? "A" : up}</th>)}</tr></thead>
      <tbody>{Object.entries(table).map(([value, row]) => {
        const total = Number(value);
        const label = kind === "pair" ? `${total === 11 ? "A" : total}, ${total === 11 ? "A" : total}` : kind === "soft" ? `${total} (A + ${total === 12 ? "A" : total - 11})` : value;
        return <tr key={value}><th scope="row">{label}</th>{row.map((cell, index) => <td className={`bj-cell-${cell}`} key={index} title={`${label} vs ${UPCARDS[index] === 11 ? "Ace" : UPCARDS[index]}: ${CELL_LABELS[cell]}`}><abbr title={CELL_LABELS[cell]}>{cell}</abbr></td>)}</tr>;
      })}</tbody>
    </table>
  </div>;
}

export function StrategyChart() {
  return <div className="bj-chart-content">
    <p className="bj-copy">{RULESET_SUMMARY}. Columns show the dealer’s upcard. Use pairs when splitting is available, otherwise the soft or hard total.</p>
    <div className="bj-chart-key">{Object.entries(CELL_LABELS).map(([cell, label]) => <span key={cell}><b className={`bj-cell-${cell}`}>{cell}</b>{label}</span>)}</div>
    <p className="bj-copy">After a hit, doubling and surrender are unavailable. After a split, surrender is unavailable. At the four-hand limit or without chips to split, use the total. Split aces stop automatically. Soft 12 is A + A when splitting is unavailable; 21 always ends the turn. Decline insurance and even money.</p>
    <ChartTable title="Hard totals" table={HARD} kind="hard"/>
    <ChartTable title="Soft totals" table={SOFT} kind="soft"/>
    <ChartTable title="Pairs · equal-value 10s include J, Q, K" table={PAIRS} kind="pair"/>
    <p className="bj-copy">This is total-dependent basic strategy. It does not use card counting or composition-dependent exceptions. A correct decision improves the long-run expectation; it cannot promise the next result.</p>
  </div>;
}

export function Reference({ inline = false }: { inline?: boolean }) {
  return <details className={`bj-reference${inline ? " bj-reference-inline" : ""}`}>
    <summary>Basic Strategy Chart <span aria-hidden="true">↗</span></summary>
    <StrategyChart/>
  </details>;
}

export function TableRules() {
  return <details className="bj-rules"><summary>{RULESET_NAME} table rules & sources</summary><p>{RULESET_NOTE}</p><ul>{RULE_SUMMARY.map(rule => <li key={rule}>{rule}</li>)}</ul>
    <p>Rule references and strategy checked on 30 October 2026. These sources describe different table variants; this lab uses the rules listed above.</p><div className="bj-source-links">
      <a href="https://www.ballycasino.com/table-games/atlantic-city-blackjack" target="_blank" rel="noopener noreferrer">Atlantic City rules & split limits ↗</a>
      <a href="https://cms.rationalcdn.com/v3/assets/blteecf9626d9a38b03/blt071c95737e1f2ec3/65d5c20c5b87e7631559ed7f/GameRules_AtlanticCityBlackjackPA_En.pdf" target="_blank" rel="noopener noreferrer">Eight-deck S17 & surrender rules (PDF) ↗</a>
      <a href="https://casino.hardrock.com/atlantic-city/casino/table-games/blackjack-at-hard-rock-atlantic-city" target="_blank" rel="noopener noreferrer">Casino table variations ↗</a>
      <a href="https://wizardofodds.com/games/blackjack/strategy/4-decks/" target="_blank" rel="noopener noreferrer">4–8 deck S17 strategy ↗</a>
    </div>
  </details>;
}
