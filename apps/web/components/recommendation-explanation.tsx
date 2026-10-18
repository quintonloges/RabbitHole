import Link from "next/link";
import type { Item } from "@/lib/types";

export function RecommendationExplanation({ item }: { item: Item }) {
  if (!item.reason) return null;
  const rationale = item.rationale;
  return <div className="recommendation-reason">
    {rationale?.direction && <span className="eyebrow">{rationale.direction}</span>}
    <p className="subtle">{item.reason}</p>
    {item.revisit && <p className="subtle">Already in this trail · revisit intentionally</p>}
    <details className="details"><summary>Why this?</summary>
      {rationale?.detail && <p>{rationale.detail}</p>}
      {rationale?.evidence.map((evidence, index) => <p key={`${evidence.itemId}-${index}`}>
        {evidence.itemId === item.id ? <span>This source</span> : <Link className="text-link" href={`/items/${evidence.itemId}`}>{evidence.title}</Link>}
        {evidence.match ? <> · {evidence.field === "user-tag" ? "your tag" : evidence.field === "source-text" ? "text match" : `${evidence.field} match`}: {evidence.match}</> : evidence.concept ? <> · {evidence.concept}</> : null}
        {evidence.relationship === "saved" ? " · saved when selected" : evidence.relationship === "read" ? " · marked read when selected" : evidence.relationship === "user-trail" ? " · chosen for a trail" : evidence.relationship === "positive-feedback" ? " · More like this feedback" : null}
      </p>)}
      {!rationale && <p>{item.reason}</p>}
    </details>
  </div>;
}
