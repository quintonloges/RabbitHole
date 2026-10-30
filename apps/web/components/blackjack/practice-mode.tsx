"use client";
import { useEffect, useRef, useState } from "react";
import { ArrowRight, RotateCcw } from "lucide-react";
import { DRILLS, type Drill, type Scenario, decisionFor, practiceBag } from "@/lib/blackjack/practice";
import type { Action } from "@/lib/blackjack/engine";
import { coach, type Feedback } from "@/lib/blackjack/coaching";
import { money } from "@/lib/blackjack/rules";
import { Cards } from "./cards";
import { ActionButtons, CoachFeedback } from "./shared";
import { Reference } from "./reference";

export function PracticeMode() {
  const [category, setCategory] = useState<Drill>("All Hands");
  const [bag, setBag] = useState<Scenario[]>([]);
  const [index, setIndex] = useState(0);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [counts, setCounts] = useState({ correct: 0, attempted: 0 });
  const firstAction = useRef<HTMLDivElement>(null);
  const answered = useRef(false);
  useEffect(() => { setBag(practiceBag("All Hands")); }, []);
  function restart(nextCategory = category) {
    setCategory(nextCategory); setBag(practiceBag(nextCategory)); setIndex(0); setFeedback(null); answered.current = false; setCounts({ correct: 0, attempted: 0 });
  }
  function answer(action: Action) {
    if (answered.current || !bag[index]) return;
    answered.current = true;
    const result = coach(decisionFor(bag[index]), action);
    setFeedback(result); setCounts(previous => ({ correct: previous.correct + Number(result.correct), attempted: previous.attempted + 1 }));
  }
  function next() {
    if (index + 1 < bag.length) setIndex(index + 1);
    else {
      const nextBag = practiceBag(category);
      if (nextBag[0]?.id === bag[index]?.id) nextBag.push(nextBag.shift()!);
      setBag(nextBag); setIndex(0);
    }
    answered.current = false; setFeedback(null);
    requestAnimationFrame(() => firstAction.current?.querySelector("button")?.focus({ preventScroll: true }));
  }
  const item = bag[index];
  return <div className="bj-practice">
    <div className="bj-mode-heading"><div><h2>One hand. One decision.</h2><p>Choose the strongest available move. Then deal yourself another.</p></div><div className="bj-score" aria-label="Practice score"><strong>{counts.correct}<span> / {counts.attempted}</span></strong><small>correct / attempted</small></div></div>
    <div className="bj-practice-toolbar"><label className="field">Drill category<select value={category} onChange={event => restart(event.target.value as Drill)}>{DRILLS.map(drill => <option key={drill}>{drill}</option>)}</select></label><button className="btn ghost" onClick={() => restart()}><RotateCcw size={15}/> Restart session</button></div>
    <div className="bj-felt bj-drill-table">
      {item ? <><Cards cards={[item.dealer]} label="Dealer upcard" total={false}/><div className="bj-table-rule">DEALER BLACKJACK RULED OUT</div><Cards cards={item.hand.cards} label="Your hand"/>
        <p className="bj-table-note">{item.hand.fromSplit ? "After a split" : item.hand.cards.length > 2 ? "After a hit" : "Original two-card hand"} · {money(item.hand.bet)} wager · {money(item.bankroll)} available</p></> : <p role="status">Preparing your practice hands…</p>}
    </div>
    <div className="bj-decision-area" ref={firstAction}>{item && <ActionButtons actions={decisionFor(item).legal} onAction={answer} disabled={feedback !== null}/>}<CoachFeedback feedback={feedback}/>{feedback && <button className="btn primary" onClick={next}>Next hand <ArrowRight size={16}/></button>}</div>
    <Reference/>
  </div>;
}
