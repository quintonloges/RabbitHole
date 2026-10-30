import type { Metadata } from "next";
import { BlackjackLab } from "@/components/blackjack/blackjack-lab";
import "./blackjack.css";

export const metadata: Metadata = {
  title: "Blackjack Lab · RabbitHole",
  description: "Learn blackjack, practice basic strategy, and play with a reliable coach. Seven interactive lessons and a six-deck S17 table with virtual chips.",
};

export default function BlackjackPage() { return <BlackjackLab/>; }
