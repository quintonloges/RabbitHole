"use client";
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { ChevronUp, CircleHelp, X } from "lucide-react";
import { isBlackjack } from "@/lib/blackjack/cards";
import { type Action, type Game, act, canInsure, deal, dealerStep, insuranceDecision, legalActions, newGame } from "@/lib/blackjack/engine";
import { type Feedback, coach, coachInsurance } from "@/lib/blackjack/coaching";
import { money, RULES, RULESET_NAME } from "@/lib/blackjack/rules";
import { tableMotion, type TableFrame, type TableMove } from "@/lib/blackjack/table-motion";
import { Cards, PlayingCard } from "./cards";
import { ActionButtons, CoachFeedback } from "./shared";
import { Reference } from "./reference";
import { BettingDrawer } from "./betting-drawer";

const OUTCOMES = { blackjack: "Blackjack · pays 3:2", win: "Win", loss: "Loss", push: "Push", bust: "Bust", surrender: "Surrender · half returned" };

export function PlayMode({ active, coaching, onCoaching }: { active: boolean; coaching: boolean; onCoaching: (enabled: boolean) => void }) {
  const [game, setGame] = useState<Game>(() => newGame());
  const current = useRef(game);
  const [wager, setWager] = useState("10");
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [error, setError] = useState("");
  const [frames, setFrames] = useState<TableFrame[]>([]);
  const [reducedMotion, setReducedMotion] = useState(false);
  const busy = useRef(false);
  const table = useRef<HTMLDivElement>(null);
  const insuranceLayer = useRef<HTMLDivElement>(null);
  const decisions = useRef<HTMLDivElement>(null);
  const pendingFocus = useRef<HTMLElement | null>(null);
  const insuranceDialog = useRef<HTMLDialogElement>(null);
  const declineInsurance = useRef<HTMLButtonElement>(null);
  const insuranceTrigger = useRef<HTMLButtonElement>(null);
  const [insuranceDismissed, setInsuranceDismissed] = useState(false);
  const [insuranceDetails, setInsuranceDetails] = useState(false);
  const wagerDialog = useRef<HTMLDialogElement>(null);
  const wagerTrigger = useRef<HTMLButtonElement>(null);
  const wagerDeal = useRef<HTMLButtonElement>(null);
  const wagerFocusRequested = useRef(false);
  const [wagerDismissed, setWagerDismissed] = useState(true);
  const animation = frames[0];
  const needsInsurance = game.phase === "insurance" && !animation;
  const showInsurance = active && needsInsurance && !insuranceDismissed;
  const betting = !animation && (game.phase === "betting" || game.phase === "settled");
  const showWager = active && betting && !wagerDismissed;
  const wagerCents = Number(wager) * 100;
  const hideHole = animation?.hideHole ?? (game.phase === "player" || game.phase === "insurance");
  const committed = game.hands.reduce((sum, hand) => sum + hand.bet, 0) + game.insurance;
  const available = animation?.bankroll ?? game.bankroll;
  const commit = useCallback((next: Game, move: TableMove) => {
    if (move !== "dealer") {
      const focused = document.activeElement;
      pendingFocus.current = focused instanceof HTMLElement && (decisions.current?.contains(focused) || insuranceDialog.current?.contains(focused)) ? focused : null;
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
  useLayoutEffect(() => {
    const felt = table.current, layer = insuranceLayer.current;
    if (!active || !needsInsurance || !felt || !layer) return;
    const position = () => {
      const dealerCards = Array.from(felt.querySelectorAll(".bj-dealer .bj-card"));
      const playerCards = Array.from(felt.querySelectorAll(".bj-player-hand .bj-card"));
      if (!dealerCards.length || !playerCards.length) return;
      const upper = Math.max(...dealerCards.map(card => card.getBoundingClientRect().bottom));
      const lower = Math.min(...playerCards.map(card => card.getBoundingClientRect().top));
      const origin = felt.getBoundingClientRect().top + felt.clientTop;
      const center = (upper + lower) / 2 - origin;
      // Float over the existing gap; opening the lightbox never moves a hand.
      layer.style.setProperty("--bj-insurance-center", `${center}px`);
      layer.style.setProperty("--bj-insurance-space", `${Math.max(0, lower - upper - 12)}px`);
      layer.style.setProperty("--bj-insurance-expanded-space", `${Math.max(0, 2 * Math.min(center, felt.clientHeight - center) - 24)}px`);
    };
    position();
    const observer = new ResizeObserver(position);
    observer.observe(felt);
    window.addEventListener("resize", position);
    return () => { observer.disconnect(); window.removeEventListener("resize", position); };
  }, [active, needsInsurance]);
  useEffect(() => {
    if (!showInsurance) return;
    insuranceDialog.current?.scrollIntoView({ block: "center", inline: "nearest", behavior: "instant" });
    declineInsurance.current?.focus({ preventScroll: true });
  }, [showInsurance]);
  useEffect(() => {
    if (showInsurance && insuranceDetails) insuranceDialog.current?.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "instant" });
  }, [showInsurance, insuranceDetails]);
  useEffect(() => {
    if (!showWager) return;
    const requested = wagerFocusRequested.current;
    wagerFocusRequested.current = false;
    // A requested opening or a completed keyboard action gets the next control.
    // Deliberate focus on the table, coaching or navigation stays where it is.
    if (!requested && document.activeElement !== document.body && document.activeElement !== pendingFocus.current) return;
    const target = wagerDeal.current && !wagerDeal.current.disabled ? wagerDeal.current
      : wagerDialog.current?.querySelector<HTMLElement>(".bj-change-wager:not(:disabled), .bj-reset");
    target?.focus({ preventScroll: true });
  }, [showWager]);
  useEffect(() => {
    if (animation || game.phase === "dealer" || !pendingFocus.current) return;
    const previous = pendingFocus.current;
    pendingFocus.current = null;
    const area = decisions.current;
    // A mode switch or a deliberate focus change during motion takes precedence.
    if (!area || area.closest("[hidden]") || (document.activeElement !== document.body && document.activeElement !== previous)) return;
    const target = area.contains(previous) && !previous.matches(":disabled") ? previous
      : area.querySelector<HTMLElement>(".bj-actions button:not(:disabled), .bj-deal:not(:disabled)")
        ?? area.querySelector<HTMLElement>(".bj-wager-actions button:not(:disabled), button:not(:disabled)");
    target?.focus({ preventScroll: true });
  }, [animation, game]);

  function startRound() {
    if (busy.current) return;
    try { commit(deal(current.current, wagerCents), "deal"); setInsuranceDismissed(false); setInsuranceDetails(false); setWagerDismissed(false); setFeedback(null); setError(""); }
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
  function reset() { const next = newGame(); current.current = next; busy.current = false; setFrames([]); setGame(next); setFeedback(null); setError(""); setWager("10"); requestAnimationFrame(() => wagerDeal.current?.focus({ preventScroll: true })); }
  function dismissInsurance() {
    setInsuranceDismissed(true);
    setInsuranceDetails(false);
    requestAnimationFrame(() => insuranceTrigger.current?.focus({ preventScroll: true }));
  }
  function dismissWager() {
    setWagerDismissed(true);
    requestAnimationFrame(() => wagerTrigger.current?.focus({ preventScroll: true }));
  }
  const dealerBlackjack = !hideHole && isBlackjack(game.dealer);
  return <div className="bj-play">
    <div className="bj-mode-heading"><div><h2>Take a seat.</h2><p>Virtual chips, real decisions. The table follows your lead.</p></div><button className={`bj-coach-toggle${coaching ? " bj-coach-enabled" : ""}`} role="switch" aria-checked={coaching} aria-label="Coach" onClick={() => { onCoaching(!coaching); setFeedback(null); }}><span className="bj-switch" aria-hidden="true"/><span>Coach <b>{coaching ? "On" : "Off"}</b></span></button></div>
    <div className="bj-bank-bar"><div><span>Available chips</span><strong className="bj-bank-value" key={available} data-testid="blackjack-bankroll">{money(available)}</strong></div><div><span>{game.phase === "settled" && !animation ? "Round wagered" : "On the table"}</span><strong>{money(committed)}</strong></div><div><span>Table limits</span><strong>$5–$500</strong></div></div>
    <div className="bj-felt bj-play-table" ref={table} aria-label="Blackjack table" aria-busy={!!animation || game.phase === "dealer"}>
      <div className="bj-dealer">{game.dealer.length ? <Cards cards={game.dealer} hideHole={hideHole} visibleCards={animation?.visibleCards} label="Dealer" totalLabel={dealerBlackjack ? "Blackjack · 21" : undefined}/> : <><div className="bj-label">Dealer</div><div className="bj-cards"><PlayingCard hidden/><PlayingCard hidden/></div><span className="bj-total">Waiting for your wager</span></>}</div>
      <div className="bj-felt-lettering"><span>BLACKJACK PAYS 3 TO 2</span><small>{RULESET_NAME} · {RULES.decks} decks · Dealer stands on soft 17</small></div>
      {needsInsurance && <div className="bj-insurance-layer" ref={insuranceLayer} data-open={showInsurance}>
        {showInsurance ? <dialog open ref={insuranceDialog} className="bj-insurance" data-expanded={insuranceDetails} aria-modal="false" aria-labelledby="bj-insurance-title" aria-describedby="bj-insurance-description" onKeyDown={event => {
          if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); dismissInsurance(); }
        }}>
          <div className="bj-insurance-heading">
            <h3 id="bj-insurance-title">Insurance?</h3>
            <button type="button" className="bj-insurance-icon" aria-label="How insurance works" title="How insurance works" aria-expanded={insuranceDetails} aria-controls={insuranceDetails ? "bj-insurance-details" : undefined} onClick={() => setInsuranceDetails(!insuranceDetails)}><CircleHelp size={18}/></button>
            <button type="button" className="bj-insurance-icon" aria-label="Close insurance decision" onClick={dismissInsurance}><X size={18}/></button>
          </div>
          <div className="bj-insurance-copy" tabIndex={insuranceDetails ? 0 : undefined}>
            <p id="bj-insurance-description">{canInsure(game) ? <>Bet {money(game.hands[0].bet / 2)} on dealer blackjack. Pays 2:1.</> : "Not enough available chips for insurance."}</p>
            {insuranceDetails && <div id="bj-insurance-details" className="bj-insurance-details">
              <p>If the dealer has no blackjack, this separate wager loses.</p>
              <p>The dealer checks next. Blackjack ends the round whether you take insurance or decline it; otherwise your main hand continues normally.</p>
              {isBlackjack(game.hands[0].cards) && <p>You have a natural. Taking insurance gives the same net profit as accepting even money.</p>}
            </div>}
          </div>
          <div className="bj-actions"><button className="btn" ref={declineInsurance} aria-label="Decline insurance" onClick={() => insure(false)}>Decline</button><button className="btn" disabled={!canInsure(game)} aria-label={`Take insurance · ${money(game.hands[0].bet / 2)}`} onClick={() => insure(true)}>Insure · {money(game.hands[0].bet / 2)}</button></div>
        </dialog> : <button ref={insuranceTrigger} className="btn bj-insurance-trigger" onClick={() => setInsuranceDismissed(false)}>Decide on insurance</button>}
      </div>}
      {game.hands.length ? <div className={`bj-player-hands${game.hands.length > 1 ? " bj-split-hands" : ""}`}>
        {game.hands.map((hand, index) => {
          const active = !animation && game.phase === "player" && game.activeHand === index;
          return <section className={`bj-player-hand${active ? " bj-active-hand" : ""}`} key={hand.id} aria-label={`Player hand ${index + 1}${active ? ", active" : ""}`}>
            <div className="bj-hand-heading"><span>{game.hands.length > 1 ? `Hand ${index + 1}` : "Your hand"}</span><span className="bj-hand-bet" key={hand.bet}>{money(hand.bet)}</span></div>
            <Cards cards={hand.cards} visibleCards={animation?.visibleCards} totalLabel={isBlackjack(hand.cards, hand.fromSplit) ? "Blackjack · 21" : undefined}/><span className="bj-hand-state">{animation ? "\u00a0" : hand.outcome ? OUTCOMES[hand.outcome] : active ? "Your turn" : hand.status === "waiting" ? "Next · awaiting second card" : hand.splitAces ? "Split Ace · one card only" : hand.status === "stood" ? "Standing" : hand.status === "busted" ? "Bust" : hand.status === "surrendered" ? "Surrendered" : game.phase === "insurance" ? "Insurance decision" : "Waiting for dealer"}</span>
            {!animation && hand.returned !== undefined && <small>Hand return {money(hand.returned)} · Hand net {hand.returned === hand.bet ? "" : hand.returned > hand.bet ? "+" : "−"}{money(Math.abs(hand.returned - hand.bet))}</small>}
          </section>;
        })}
      </div> : <div className="bj-seat"><span aria-hidden="true">♠</span><p>Your next hand starts here.</p><small>Choose a wager, then deal.</small></div>}
      <div className="bj-table-status" role="status" aria-live="polite">{animation?.message ?? (game.phase === "dealer" ? "Dealer plays…" : game.phase === "player" && game.peeked ? "Dealer checked: no blackjack." : game.phase === "settled" ? dealerBlackjack ? "Dealer has blackjack. The round ends immediately, before you hit or stand." : "Round complete." : game.phase === "insurance" ? "Dealer shows an Ace. Insurance comes before the peek." : "")}</div>
    </div>
    <div className="bj-decision-area" ref={decisions}>
      {game.phase === "player" && <><ActionButtons actions={legalActions(game)} onAction={choose} disabled={!!animation}/>{!animation && game.bankroll < game.hands[game.activeHand].bet && <p className="bj-copy">Extra split or double bets need another {money(game.hands[game.activeHand].bet)}. The coach uses your available actions.</p>}</>}
      {!animation && game.phase !== "settled" && game.insuranceResult === "lost" && <p className="bj-insurance-result">Insurance lost: {money(game.insurance)}. Your main bet is still in play.</p>}
      {coaching && <CoachFeedback feedback={feedback}/>}
      {error && !betting && <div className="error" role="alert">{error} <button className="btn small" onClick={reset}>Reset table</button></div>}
      {betting && !showWager && <button className="btn bj-wager-trigger" ref={wagerTrigger} aria-haspopup="dialog" aria-expanded={false} aria-controls="bj-wager-drawer" onClick={() => { wagerFocusRequested.current = true; setWagerDismissed(false); }}><span>{game.phase === "settled" ? "Bet again" : "Place your wager"}</span><ChevronUp size={18} aria-hidden="true"/></button>}
      <BettingDrawer key={game.hands[0]?.id ?? "new"} game={game} available={available} wager={wager} open={showWager} error={betting ? error : ""} drawerRef={wagerDialog} dealRef={wagerDeal} onWager={setWager} onDeal={startRound} onReset={reset} onClose={dismissWager}/>
    </div>
    <Reference/>
  </div>;
}
