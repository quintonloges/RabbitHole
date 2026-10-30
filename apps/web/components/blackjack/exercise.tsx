"use client";
import { useState } from "react";
import { exampleCards, handLabel, isBlackjack } from "@/lib/blackjack/cards";
import { type Exercise as ExerciseData, type DemoAction, DEMO_DEALS, teachingShoe } from "@/lib/blackjack/lessons";
import { type Action, act, deal, dealerStep, insuranceDecision, newGame } from "@/lib/blackjack/engine";
import { coach, type Feedback } from "@/lib/blackjack/coaching";
import { decisionFor, scenario } from "@/lib/blackjack/practice";
import { money } from "@/lib/blackjack/rules";
import { ACTION_LABELS } from "@/lib/blackjack/strategy";
import { Cards } from "./cards";
import { ActionButtons, CoachFeedback } from "./shared";

export function Exercise({ exercise, onAttempt }: { exercise: ExerciseData; onAttempt: () => void }) {
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  function answer(choice: string) {
    if (selected !== null) return;
    setSelected(choice); onAttempt();
    if (exercise.kind === "decision") {
      const item = scenario(exercise.cards, exercise.dealer, { fromSplit: exercise.fromSplit, bankroll: exercise.bankroll });
      setFeedback(coach(decisionFor(item), choice as Action));
    } else {
      const expected = exercise.kind === "value" ? handLabel(exampleCards(exercise.cards)) : exercise.choices[exercise.answer];
      setFeedback({ correct: choice === expected, title: choice === expected ? `Correct — ${expected}.` : `Answer: ${expected}.`, explanation: exercise.kind === "value" ? "Use 11 for an Ace when the total stays at 21 or below. Otherwise count it as 1. A hand is soft only while an Ace counts as 11." : exercise.explanation, context: `You chose ${choice}` });
    }
  }
  const item = exercise.kind === "decision" ? scenario(exercise.cards, exercise.dealer, { fromSplit: exercise.fromSplit, bankroll: exercise.bankroll }) : null;
  const dealerCards = exercise.kind === "quiz" && exercise.dealerCards ? exampleCards(exercise.dealerCards) : null;
  return <div className="bj-exercise">
    <div className="bj-label">Try it</div><h4>{exercise.kind === "decision" ? "What is the best available move?" : exercise.kind === "value" ? "How do you value this hand?" : exercise.question}</h4>
    {(item || exercise.cards || dealerCards) && <div className="bj-exercise-hands">
      {item && <Cards cards={[item.dealer]} label="Dealer upcard" total={false}/>}
      {dealerCards && <Cards cards={dealerCards} label="Dealer hand" totalLabel={isBlackjack(dealerCards) ? "Blackjack · 21" : undefined}/>}
      {exercise.cards && <Cards cards={exampleCards(exercise.cards)} label={item || dealerCards ? "Your hand" : undefined} total={exercise.kind === "decision" || dealerCards !== null}/>}
    </div>}
    {item && <p className="bj-copy">{item.hand.fromSplit ? "After a split. " : "Original hand. "}{item.hand.cards.length > 2 ? "You have already hit. " : ""}Wager {money(item.hand.bet)} · {money(item.bankroll)} available. Dealer blackjack ruled out.</p>}
    {item ? <ActionButtons actions={decisionFor(item).legal} onAction={answer} disabled={selected !== null}/>
      : <div className="bj-actions">{exercise.kind !== "decision" && exercise.choices.map(choice => <button key={choice} className={`btn${selected === choice ? " accent" : ""}`} disabled={selected !== null} onClick={() => answer(choice)}>{choice}</button>)}</div>}
    <CoachFeedback feedback={feedback}/>
  </div>;
}

export function ActionDemo({ action }: { action: DemoAction }) {
  const [game, setGame] = useState(() => deal(newGame(teachingShoe(DEMO_DEALS[action])), 1000));
  const [tried, setTried] = useState(false);
  const [message, setMessage] = useState("");
  function demonstrate() {
    let next = action === "insurance" ? insuranceDecision(game, true) : act(game, action);
    while (next.phase === "dealer") next = dealerStep(next);
    setGame(next); setTried(true);
    setMessage(action === "split" ? "Two $10 hands. Hand 1 is active; hand 2 waits for its second card."
      : action === "insurance" ? "Insurance wins: $5 stake plus $10 profit returns. The main $10 wager loses to dealer blackjack."
      : action === "surrender" ? "Half the $10 wager returns: a $5 loss."
      : action === "double" ? "One card added; the $20 hand stood automatically. The dealer busted, so $40 returned."
      : action === "stand" ? "Your 18 stayed in place. The dealer drew and busted; $20 returned."
      : "One card added, with no extra wager. You could now hit or stand.");
  }
  return <div className="bj-demo"><div className="bj-label">Guided example · $10 opening wager</div><div className="bj-demo-hands">
    <Cards cards={game.dealer} hideHole={game.phase === "player" || game.phase === "insurance"} label="Dealer"/>
    <div className="bj-demo-player-hands">{game.hands.map((hand, index) => <Cards key={hand.id} cards={hand.cards} label={`${game.hands.length > 1 ? `Hand ${index + 1}` : "Your hand"} · ${money(hand.bet)}`}/>)}</div>
  </div><p className="bj-copy">Available chips: {money(game.bankroll)}</p>
    {!tried ? <button className="btn" onClick={demonstrate}>Try {action === "insurance" ? "insurance" : ACTION_LABELS[action].toLowerCase()}</button> : <button className="btn small" onClick={() => { setGame(deal(newGame(teachingShoe(DEMO_DEALS[action])), 1000)); setTried(false); setMessage(""); }}>Replay example</button>}
    <p className="bj-copy" role="status">{message}</p>
  </div>;
}
