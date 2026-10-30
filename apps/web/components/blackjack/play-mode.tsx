"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import { RotateCcw } from "lucide-react";
import { isBlackjack } from "@/lib/blackjack/cards";
import { type Action, type Game, act, canInsure, deal, dealerStep, insuranceDecision, legalActions, newGame, validBet } from "@/lib/blackjack/engine";
import { type Feedback, coach, coachInsurance } from "@/lib/blackjack/coaching";
import { money, RULES } from "@/lib/blackjack/rules";
import { tableMotion, type TableFrame, type TableMove } from "@/lib/blackjack/table-motion";
import { Cards, PlayingCard } from "./cards";
import { ActionButtons, CoachFeedback } from "./shared";
import { Reference } from "./reference";
import { RoundResult } from "./round-result";

const OUTCOMES = { blackjack: "Blackjack · pays 3:2", win: "Win", loss: "Loss", push: "Push", bust: "Bust", surrender: "Surrender · half returned" };

export function PlayMode({ coaching, onCoaching }: { coaching: boolean; onCoaching: (enabled: boolean) => void }) {
  const [game, setGame] = useState<Game>(() => newGame());
  const current = useRef(game);
  const [wager, setWager] = useState("10");
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [error, setError] = useState("");
  const [frames, setFrames] = useState<TableFrame[]>([]);
  const [reducedMotion, setReducedMotion] = useState(false);
  const busy = useRef(false);
  const decisions = useRef<HTMLDivElement>(null);
  const pendingFocus = useRef<HTMLElement | null>(null);
  const animation = frames[0];
  const betting = !animation && (game.phase === "betting" || game.phase === "settled");
  const wagerCents = Number(wager) * 100;
  const canDeal = validBet(wagerCents, game.bankroll);
  const hideHole = animation?.hideHole ?? (game.phase === "player" || game.phase === "insurance");
  const committed = game.hands.reduce((sum, hand) => sum + hand.bet, 0) + game.insurance;
  const available = animation?.bankroll ?? game.bankroll;
  const commit = useCallback((next: Game, move: TableMove) => {
    if (move !== "dealer") {
      const focused = document.activeElement;
      pendingFocus.current = focused instanceof HTMLElement && decisions.current?.contains(focused) ? focused : null;
    }
    const sequence = window.matchMedia("(prefers-reduced-motion: reduce)").matches ? [] : tableMotion(current.current, next, move);
    busy.current = sequence.length > 0;
    current.current = next; setGame(next); setFrames(sequence);
  }, []);
  useEffect(() => {
    const media = window.matchMedia("(prefers-reduced-motion: reduce)");
    const update = () => {
      setReducedMotion(media.matches);
      if (media.matches) { busy.current = false; setFrames([]); }
    };
    update(); media.addEventListener("change", update);
    return () => media.removeEventListener("change", update);
  }, []);
  useEffect(() => {
    if (!animation) return;
    const timer = window.setTimeout(() => {
      busy.current = frames.length > 1;
      setFrames(frames.slice(1));
    }, animation.duration);
    return () => window.clearTimeout(timer);
  }, [animation, frames]);
  useEffect(() => {
    if (game.phase !== "dealer" || animation) return;
    const timer = window.setTimeout(() => {
      if (current.current !== game || busy.current) return;
      try { commit(dealerStep(game), "dealer"); }
      catch (cause) { setError(cause instanceof Error ? cause.message : "Could not complete the dealer's turn."); }
    }, reducedMotion ? 0 : 180);
    return () => window.clearTimeout(timer);
  }, [game, animation, reducedMotion, commit]);
  useEffect(() => {
    if (animation || game.phase === "dealer" || !pendingFocus.current) return;
    const previous = pendingFocus.current;
    pendingFocus.current = null;
    const area = decisions.current;
    // A mode switch or a deliberate focus change during motion takes precedence.
    if (!area || area.closest("[hidden]") || (document.activeElement !== document.body && document.activeElement !== previous)) return;
    const target = area.contains(previous) && !previous.matches(":disabled") ? previous
      : area.querySelector<HTMLElement>(".bj-actions button:not(:disabled), .bj-deal:not(:disabled)")
        ?? area.querySelector<HTMLElement>(".bj-betting input, button:not(:disabled)");
    target?.focus({ preventScroll: true });
  }, [animation, game]);

  function startRound() {
    if (busy.current) return;
    try { commit(deal(current.current, wagerCents), "deal"); setFeedback(null); setError(""); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Could not deal."); }
  }
  function choose(action: Action) {
    if (busy.current) return;
    const before = current.current;
    if (!legalActions(before).includes(action)) return;
    const hand = before.hands[before.activeHand];
    // Capture feedback BEFORE drawing. No outcome or hole-card information enters the coach.
    const assessment = coaching ? coach({ cards: hand.cards, dealerUpcard: before.dealer[0], legal: legalActions(before) }, action) : null;
    try { commit(act(before, action), action); setFeedback(assessment); setError(""); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Could not play that action."); }
  }
  function insure(accept: boolean) {
    if (busy.current) return;
    try { commit(insuranceDecision(current.current, accept), "insurance"); setFeedback(coaching ? coachInsurance(accept) : null); setError(""); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Could not place insurance."); }
  }
  function reset() { const next = newGame(); current.current = next; busy.current = false; setFrames([]); setGame(next); setFeedback(null); setError(""); setWager("10"); }
  const dealerBlackjack = !hideHole && isBlackjack(game.dealer);
  return <div className="bj-play">
    <div className="bj-mode-heading"><div><h2>Take a seat.</h2><p>Virtual chips, real decisions. The table follows your lead.</p></div><button className={`bj-coach-toggle${coaching ? " bj-coach-enabled" : ""}`} role="switch" aria-checked={coaching} aria-label="Coach" onClick={() => { onCoaching(!coaching); setFeedback(null); }}><span className="bj-switch" aria-hidden="true"/><span>Coach <b>{coaching ? "On" : "Off"}</b></span></button></div>
    <div className="bj-bank-bar"><div><span>Available chips</span><strong className="bj-bank-value" key={available} data-testid="blackjack-bankroll">{money(available)}</strong></div><div><span>{game.phase === "settled" && !animation ? "Round wagered" : "On the table"}</span><strong>{money(committed)}</strong></div><div><span>Table limits</span><strong>$5–$500</strong></div></div>
    <div className="bj-felt bj-play-table" aria-label="Blackjack table" aria-busy={!!animation || game.phase === "dealer"}>
      <div className="bj-dealer">{game.dealer.length ? <Cards cards={game.dealer} hideHole={hideHole} visibleCards={animation?.visibleCards} label="Dealer" totalLabel={dealerBlackjack ? "Blackjack · 21" : undefined}/> : <><div className="bj-label">Dealer</div><div className="bj-cards"><PlayingCard hidden/><PlayingCard hidden/></div><span className="bj-total">Waiting for your wager</span></>}</div>
      <div className="bj-felt-lettering"><span>BLACKJACK PAYS 3 TO 2</span><small>Six decks · Dealer stands on soft 17</small></div>
      {game.hands.length ? <div className={`bj-player-hands${game.hands.length > 1 ? " bj-split-hands" : ""}`}>
        {game.hands.map((hand, index) => {
          const active = !animation && game.phase === "player" && game.activeHand === index;
          return <section className={`bj-player-hand${active ? " bj-active-hand" : ""}`} key={hand.id} aria-label={`Player hand ${index + 1}${active ? ", active" : ""}`}>
            <div className="bj-hand-heading"><span>{game.hands.length > 1 ? `Hand ${index + 1}` : "Your hand"}</span><span className="bj-hand-bet" key={hand.bet}>{money(hand.bet)}</span></div>
            <Cards cards={hand.cards} visibleCards={animation?.visibleCards} totalLabel={isBlackjack(hand.cards, hand.fromSplit) ? "Blackjack · 21" : undefined}/><span className="bj-hand-state">{animation ? "\u00a0" : hand.outcome ? OUTCOMES[hand.outcome] : active ? "Your turn" : hand.status === "waiting" ? "Next · awaiting second card" : hand.splitAces ? "Split Ace · one card only" : hand.status === "stood" ? "Standing" : hand.status === "busted" ? "Bust" : hand.status === "surrendered" ? "Surrendered" : game.phase === "insurance" ? "Insurance decision" : "Waiting for dealer"}</span>
            {!animation && hand.returned !== undefined && <small>Hand return {money(hand.returned)} · Hand net {hand.returned === hand.bet ? "" : hand.returned > hand.bet ? "+" : "−"}{money(Math.abs(hand.returned - hand.bet))}</small>}
          </section>;
        })}
      </div> : <div className="bj-seat"><span aria-hidden="true">♠</span><p>Your next hand starts here.</p><small>Choose a wager below, then deal.</small></div>}
      <div className="bj-table-status" role="status" aria-live="polite">{animation?.message ?? (game.phase === "dealer" ? "Dealer plays…" : game.phase === "player" && game.peeked ? "Dealer checked: no blackjack." : game.phase === "settled" ? dealerBlackjack ? "Dealer has blackjack. The round ends immediately, before you hit or stand." : "Round complete." : game.phase === "insurance" ? "Dealer shows an Ace. Insurance comes before the peek." : "")}</div>
    </div>
    <div className="bj-decision-area" ref={decisions}>
      {game.phase === "insurance" && !animation && <div className="bj-insurance"><h3>Insurance?</h3><p>Bet {money(game.hands[0].bet / 2)} that the dealer has blackjack. It pays 2:1 if they do; otherwise this separate wager loses.</p><p>The dealer checks next. Blackjack ends the round whether you take insurance or decline it; otherwise your main hand continues normally.</p>{isBlackjack(game.hands[0].cards) && <p>You have a natural. Taking insurance gives the same net profit as accepting even money.</p>}<div className="bj-actions"><button className="btn" onClick={() => insure(false)}>Decline insurance</button><button className="btn" disabled={!canInsure(game)} onClick={() => insure(true)}>Take insurance · {money(game.hands[0].bet / 2)}</button></div>{!canInsure(game) && <p className="bj-copy">Not enough available chips for insurance.</p>}</div>}
      {game.phase === "player" && <><ActionButtons actions={legalActions(game)} onAction={choose} disabled={!!animation}/>{!animation && game.bankroll < game.hands[game.activeHand].bet && <p className="bj-copy">Extra split or double bets need another {money(game.hands[game.activeHand].bet)}. The coach uses your available actions.</p>}</>}
      {!animation && game.phase !== "settled" && game.insuranceResult === "lost" && <p className="bj-insurance-result">Insurance lost: {money(game.insurance)}. Your main bet is still in play.</p>}
      {!animation && <RoundResult game={game}/>}
      {coaching && <CoachFeedback feedback={feedback}/>}
      {error && <div className="error" role="alert">{error} <button className="btn small" onClick={reset}>Reset table</button></div>}
      {betting && <div className="bj-betting"><div className="bj-bet-heading"><h3>{game.phase === "settled" ? "Your next wager" : "Place your wager"}</h3><span>Virtual chips only</span></div><div className="bj-bet-controls"><div className="bj-chip-row">{[5, 10, 25, 50, 100].map(value => <button key={value} className={`bj-chip bj-chip-${value}${Number(wager) === value ? " bj-chip-selected" : ""}`} aria-label={`Bet $${value}`} aria-pressed={Number(wager) === value} disabled={value * 100 > game.bankroll} onClick={() => setWager(String(value))}><span>${value}</span></button>)}</div><label className="field">Wager ($)<input type="number" min={RULES.minimumBet / 100} max={Math.min(RULES.maximumBet, game.bankroll) / 100} step="1" inputMode="numeric" value={wager} onChange={event => setWager(event.target.value)}/></label><button className="btn primary bj-deal" disabled={!canDeal} onClick={startRound}>Deal{canDeal ? ` · ${money(wagerCents)}` : ""}</button></div>
        {!canDeal && <p className="bj-copy" role="status">{game.bankroll < RULES.minimumBet ? "Less than the $5 minimum remains. Reset your virtual chips to take another seat." : "Enter a whole-dollar wager from $5 to $500 that fits your available chips."}</p>}
        <button className="btn ghost small" onClick={reset}><RotateCcw size={14}/>Reset table · $500</button>
      </div>}
    </div>
    <Reference/>
  </div>;
}
