export const RANKS = ["2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A"] as const;
export const SUITS = ["clubs", "diamonds", "hearts", "spades"] as const;
export type Rank = typeof RANKS[number];
export type Suit = typeof SUITS[number];
export type Card = { id: string; rank: Rank; suit: Suit };
export const SUIT_SYMBOLS: Record<Suit, string> = { clubs: "♣", diamonds: "♦", hearts: "♥", spades: "♠" };

export function cardValue(card: Card): number {
  return card.rank === "A" ? 11 : ["J", "Q", "K"].includes(card.rank) ? 10 : Number(card.rank);
}

export function score(cards: readonly Card[]) {
  let total = 0;
  let aces = 0;
  for (const card of cards) {
    total += cardValue(card);
    if (card.rank === "A") aces++;
  }
  while (total > 21 && aces > 0) { total -= 10; aces--; }
  return { total, soft: aces > 0, bust: total > 21 };
}

export function isBlackjack(cards: readonly Card[], fromSplit = false) {
  return !fromSplit && cards.length === 2 && score(cards).total === 21;
}

export function pairValue(cards: readonly Card[]): number | null {
  return cards.length === 2 && cardValue(cards[0]) === cardValue(cards[1]) ? cardValue(cards[0]) : null;
}

export function shuffle<T>(items: readonly T[], random: () => number = Math.random): T[] {
  const copy = [...items];
  for (let i = copy.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [copy[i], copy[j]] = [copy[j], copy[i]];
  }
  return copy;
}

export function orderedShoe(): Card[] {
  return Array.from({ length: 6 }, (_, deck) => SUITS.flatMap(suit => RANKS.map(rank => ({ id: `${deck}-${suit}-${rank}`, rank, suit })))).flat();
}

export function createShoe(random: () => number = Math.random) { return shuffle(orderedShoe(), random); }

/** Illustrations/drills only; never inserted into a live shoe. */
export function exampleCards(ranks: readonly Rank[]): Card[] {
  return ranks.map((rank, i) => ({ id: `example-${i}-${rank}`, rank, suit: SUITS[i % 4] }));
}

export function handLabel(cards: readonly Card[]) {
  const value = score(cards);
  return value.bust ? `Bust · ${value.total}` : `${value.soft ? "Soft" : "Hard"} ${value.total}`;
}
