import { type Card, SUIT_SYMBOLS, handLabel } from "@/lib/blackjack/cards";

export function PlayingCard({ card, hidden = false }: { card?: Card; hidden?: boolean }) {
  if (hidden || !card) return <span className="bj-card bj-card-back" role="img" aria-label="Face-down card"><span aria-hidden="true">↘</span></span>;
  const red = card.suit === "hearts" || card.suit === "diamonds";
  const symbol = SUIT_SYMBOLS[card.suit];
  return <span className={`bj-card${red ? " bj-card-red" : ""}`} role="img" aria-label={`${card.rank} of ${card.suit}`}>
    <span className="bj-card-corner" aria-hidden="true"><b>{card.rank}</b><span>{symbol}</span></span>
    <span className="bj-card-pip" aria-hidden="true">{symbol}</span>
    <span className="bj-card-corner bj-card-bottom" aria-hidden="true"><b>{card.rank}</b><span>{symbol}</span></span>
  </span>;
}

export function Cards({ cards, hideHole = false, label, total = true, totalLabel }: { cards: readonly Card[]; hideHole?: boolean; label?: string; total?: boolean; totalLabel?: string }) {
  const visible = hideHole ? cards.slice(0, 1) : cards;
  return <div className="bj-hand-cards">
    {label && <div className="bj-label">{label}</div>}
    <div className="bj-cards">{cards.map((card, i) => <PlayingCard key={card.id} card={hideHole && i > 0 ? undefined : card} hidden={hideHole && i > 0}/>)}</div>
    {total && visible.length > 0 && <span className="bj-total">{hideHole ? `${visible[0].rank} showing` : totalLabel ?? handLabel(visible)}</span>}
  </div>;
}
