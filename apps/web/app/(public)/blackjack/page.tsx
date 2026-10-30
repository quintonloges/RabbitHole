import type { Metadata } from "next";
import { BlackjackLab } from "@/components/blackjack/blackjack-lab";
import { RULES } from "@/lib/blackjack/rules";
import "./blackjack.css";

export const metadata: Metadata = {
  title: "Blackjack Lab · RabbitHole",
  description: `Learn Atlantic City blackjack through seven interactive lessons, basic-strategy practice, and a coached ${RULES.decks}-deck S17 table with 3:2 payouts and virtual chips.`,
};

export default function BlackjackPage() { return <BlackjackLab/>; }
