"use client";

import { useEffect, useRef, useState, type RefObject } from "react";
import { Pencil, RotateCcw, X } from "lucide-react";
import { type Game, validBet } from "@/lib/blackjack/engine";
import { money, RULES } from "@/lib/blackjack/rules";
import { RoundResult, RoundSummary } from "./round-result";

type Props = {
  game: Game;
  available: number;
  wager: string;
  open: boolean;
  error: string;
  drawerRef: RefObject<HTMLDialogElement | null>;
  dealRef: RefObject<HTMLButtonElement | null>;
  onWager: (value: string) => void;
  onDeal: () => void;
  onReset: () => void;
  onClose: () => void;
};

export function BettingDrawer({ game, available, wager, open, error, drawerRef, dealRef, onWager, onDeal, onReset, onClose }: Props) {
  const [view, setView] = useState<"bet" | "result" | null>(null);
  const amountButton = useRef<HTMLButtonElement>(null);
  const editor = useRef<HTMLDivElement>(null);
  const content = useRef<HTMLDivElement>(null);
  const wagerCents = Number(wager) * 100;
  const canDeal = validBet(wagerCents, game.bankroll);
  const outOfChips = game.bankroll < RULES.minimumBet;
  const wagerLabel = Number.isFinite(wagerCents) ? money(wagerCents) : "—";

  useEffect(() => { if (!open) setView(null); }, [open]);
  useEffect(() => {
    content.current?.scrollTo({ top: 0, behavior: "instant" });
    if (view !== "bet" || !open) return;
    const target = editor.current?.querySelector<HTMLButtonElement>(".bj-chip-selected:not(:disabled)")
      ?? editor.current?.querySelector<HTMLButtonElement>(".bj-chip:not(:disabled)");
    target?.focus({ preventScroll: true });
  }, [view, open]);
  useEffect(() => {
    const dialog = drawerRef.current, viewport = window.visualViewport;
    if (!open || !dialog || !viewport) return;
    // Keep the action row above the mobile keyboard without moving the table.
    const resize = () => {
      dialog.style.setProperty("--bj-visible-height", `${viewport.height}px`);
      dialog.style.setProperty("--bj-drawer-bottom", `${Math.max(0, innerHeight - viewport.height - viewport.offsetTop)}px`);
    };
    resize(); viewport.addEventListener("resize", resize); viewport.addEventListener("scroll", resize);
    return () => {
      viewport.removeEventListener("resize", resize); viewport.removeEventListener("scroll", resize);
      dialog.style.removeProperty("--bj-visible-height"); dialog.style.removeProperty("--bj-drawer-bottom");
    };
  }, [open, drawerRef]);

  function finishEditing() {
    setView(null);
    requestAnimationFrame(() => amountButton.current?.focus({ preventScroll: true }));
  }
  function resetTable() { setView(null); onReset(); }

  return <dialog id="bj-wager-drawer" ref={drawerRef} open={open} className="bj-wager-drawer" aria-label={game.phase === "settled" ? "Your next wager" : "Place your wager"} aria-modal="false" onKeyDown={event => {
    if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); onClose(); }
  }}>
    <header className="bj-drawer-context">
      <div className="bj-drawer-balance"><span>Available chips</span><strong>{money(available)}</strong></div>
      {open && game.phase === "settled" && <RoundSummary game={game} expanded={view === "result"} onToggle={() => setView(view === "result" ? null : "result")}/>}
      <button type="button" className="bj-drawer-close" aria-label="Close betting" onClick={onClose}><X size={18}/></button>
    </header>

    <div className="bj-drawer-content" ref={content}>
      {view === "result" && <RoundResult game={game}/>}
      {view === "bet" && <div id="bj-wager-options" className="bj-bet-editor" ref={editor}>
        <div className="bj-chip-row" aria-label="Choose a wager">{[5, 10, 25, 50, 100].map(value => <button type="button" key={value} className={`bj-chip bj-chip-${value}${Number(wager) === value ? " bj-chip-selected" : ""}`} aria-label={`Bet $${value}`} aria-pressed={Number(wager) === value} disabled={value * 100 > game.bankroll} onClick={() => { onWager(String(value)); finishEditing(); }}><span>${value}</span></button>)}</div>
        <form className="bj-custom-wager" onSubmit={event => { event.preventDefault(); if (canDeal) finishEditing(); }}>
          <label className="field">Wager ($)<input type="number" min={RULES.minimumBet / 100} max={Math.min(RULES.maximumBet, game.bankroll) / 100} step="1" inputMode="numeric" value={wager} onChange={event => onWager(event.target.value)} onFocus={event => event.currentTarget.select()}/></label>
          <button type="submit" className="btn" disabled={!canDeal}>Done</button>
        </form>
        <button type="button" className="bj-reset" onClick={resetTable}><RotateCcw size={14}/>Reset table · $500</button>
      </div>}
      {!canDeal && <p className="bj-wager-message" role="status">{outOfChips ? "Less than the $5 minimum remains. Reset your chips to play again." : `Choose a whole-dollar bet from $5 to ${money(Math.min(RULES.maximumBet, game.bankroll))}.`}</p>}
      {error && <p className="bj-wager-message error" role="alert">{error}</p>}
    </div>

    <footer className="bj-wager-actions">
      <button type="button" className="bj-change-wager" ref={amountButton} disabled={outOfChips} aria-label={`Change bet, ${wagerLabel}`} aria-expanded={view === "bet"} aria-controls={view === "bet" ? "bj-wager-options" : undefined} onClick={() => setView(view === "bet" ? null : "bet")}><span>Bet</span><strong>{wagerLabel}</strong><Pencil size={16} aria-hidden="true"/></button>
      {outOfChips ? <button type="button" className="btn primary bj-reset" onClick={resetTable}>Reset table · $500</button>
        : <button type="button" className="btn primary bj-deal" ref={dealRef} disabled={!canDeal || !open} onClick={onDeal}>Deal hand</button>}
    </footer>
  </dialog>;
}
