import { type Card, SUIT_SYMBOLS, handLabel } from "@/lib/blackjack/cards";

export function PlayingCard({ card, hidden = false }: { card?: Card; hidden?: boolean }) {
  const face = !hidden ? card : undefined;
  const red = face?.suit === "hearts" || face?.suit === "diamonds";
  const symbol = face ? SUIT_SYMBOLS[face.suit] : "";
  return <span className={`bj-card${red ? " bj-card-red" : ""}${face ? " bj-card-face-up" : ""}`} role="img" aria-label={face ? `${face.rank} of ${face.suit}` : "Face-down card"}>
    <span className="bj-card-inner" aria-hidden="true">
      <span className="bj-card-face bj-card-front">
        {face && <><span className="bj-card-corner"><b>{face.rank}</b><span>{symbol}</span></span>
          <span className="bj-card-pip">{symbol}</span>
          <span className="bj-card-corner bj-card-bottom"><b>{face.rank}</b><span>{symbol}</span></span></>}
      </span>
      <span className="bj-card-face bj-card-back">{!face && <span>↘</span>}</span>
    </span>
  </span>;
}

export function Cards({ cards, hideHole = false, label, total = true, totalLabel, visibleCards }: { cards: readonly Card[]; hideHole?: boolean; label?: string; total?: boolean; totalLabel?: string; visibleCards?: readonly string[] }) {
  const dealt = cards.filter(card => !visibleCards || visibleCards.includes(card.id));
  const visible = hideHole ? dealt.slice(0, 1) : dealt;
  return <div className="bj-hand-cards">
    {label && <div className="bj-label">{label}</div>}
    <div className="bj-cards">{cards.map((card, i) => visibleCards && !visibleCards.includes(card.id)
      ? <span className="bj-card bj-card-pending" key={card.id} aria-hidden="true"/>
      : <PlayingCard key={card.id} card={hideHole && i > 0 ? undefined : card} hidden={hideHole && i > 0}/>)}</div>
    {total && <span className="bj-total">{visible.length > 0 ? hideHole ? `${visible[0].rank} showing` : (dealt.length === cards.length ? totalLabel : undefined) ?? handLabel(visible) : "\u00a0"}</span>}
  </div>;
}
