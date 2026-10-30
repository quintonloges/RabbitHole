import { type Game, roundTotals } from "@/lib/blackjack/engine";
import { money } from "@/lib/blackjack/rules";

function netAmount(value: number) {
  return value === 0 ? money(0) : `${value > 0 ? "+" : "−"}${money(Math.abs(value))}`;
}

export function RoundResult({ game }: { game: Game }) {
  const totals = roundTotals(game);
  if (!totals) return null;
  return <section className={`bj-round-result${totals.net > 0 ? " bj-round-win" : ""}`} aria-label="Round result">
    <div className="bj-round-heading" role="status">
      <div><span>Round result · all bets</span><strong>{totals.net === 0 ? "Even" : netAmount(totals.net)}</strong></div>
      <p>Available chips: {money(game.bankroll)}</p>
    </div>
    <table className="bj-settlement" aria-label="Round wager breakdown">
      <thead><tr><th scope="col">Wager</th><th scope="col">Bet</th><th scope="col">Returned</th><th scope="col">Net</th></tr></thead>
      <tbody>
        {game.hands.map((hand, index) => <tr key={hand.id}>
          <th scope="row">{game.hands.length === 1 ? "Main bet" : `Hand ${index + 1}`}</th>
          <td>{money(hand.bet)}</td><td>{money(hand.returned ?? 0)}</td><td>{netAmount((hand.returned ?? 0) - hand.bet)}</td>
        </tr>)}
        {game.insurance > 0 && <tr>
          <th scope="row">Insurance<small>{game.insuranceResult === "won" ? "Won · pays 2:1" : "Lost"}</small></th>
          <td>{money(game.insurance)}</td><td>{money(totals.insuranceReturned)}</td><td>{netAmount(totals.insuranceReturned - game.insurance)}</td>
        </tr>}
      </tbody>
      <tfoot><tr><th scope="row">Total</th><td>{money(totals.wagered)}</td><td>{money(totals.returned)}</td><td>{netAmount(totals.net)}</td></tr></tfoot>
    </table>
    <p className="bj-settlement-note">Returned includes any stake paid back. Net is your profit or loss.</p>
  </section>;
}
