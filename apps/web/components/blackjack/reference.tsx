import { HARD, SOFT, PAIRS, UPCARDS, CELL_LABELS, type StrategyTable } from "@/lib/blackjack/strategy";
import { RULE_SUMMARY } from "@/lib/blackjack/rules";

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
    <p className="bj-copy">Six decks · S17 · double after split · late surrender · dealer peek. Columns show the dealer’s upcard. Use pairs when splitting is available, otherwise the soft or hard total.</p>
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
  return <details className="bj-rules"><summary>Table rules & sources</summary><ul>{RULE_SUMMARY.map(rule => <li key={rule}>{rule}</li>)}</ul>
    <p>Rules and strategy verified against Wizard of Odds on 29 October 2026:</p><div className="bj-source-links">
      <a href="https://wizardofodds.com/games/blackjack/basics/" target="_blank" rel="noopener noreferrer">Game rules ↗</a>
      <a href="https://wizardofodds.com/games/blackjack/strategy/4-decks/" target="_blank" rel="noopener noreferrer">4–8 deck S17 strategy ↗</a>
      <a href="https://wizardofodds.com/games/blackjack/strategy/calculator/" target="_blank" rel="noopener noreferrer">Strategy calculator ↗</a>
      <a href="https://wizardofodds.com/games/blackjack/surrender/" target="_blank" rel="noopener noreferrer">Late surrender ↗</a>
    </div>
  </details>;
}
