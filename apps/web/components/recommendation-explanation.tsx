"use client";

import Link from "next/link";
import { Info, X } from "lucide-react";
import { useEffect, useId, useRef, useState, type CSSProperties } from "react";
import type { Item } from "@/lib/types";

export function RecommendationExplanation({ item, compact = false }: { item: Item; compact?: boolean }) {
  const id = useId();
  const [position, setPosition] = useState<CSSProperties>({});
  const [open, setOpen] = useState(false);
  const popup = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (event: Event) => {
      if (event.type === "scroll" && event.target instanceof Node && popup.current?.contains(event.target)) return;
      if (popup.current?.matches(":popover-open")) popup.current.hidePopover();
    };
    window.addEventListener("scroll", close, true);
    window.addEventListener("resize", close);
    return () => { window.removeEventListener("scroll", close, true); window.removeEventListener("resize", close); };
  }, [open]);
  if (!item.reason) return null;
  const rationale = item.rationale;
  const overview = <>
    {rationale?.direction && <span className={compact ? "reason-direction" : "eyebrow"}>{rationale.direction}</span>}
    <p className="subtle">{item.reason}</p>
    {item.revisit && <p className="subtle">Already in this trail · revisit intentionally</p>}
  </>;
  if (compact) return <div className="recommendation-reason compact-reason">
    <button className="story-action reason-trigger" popoverTarget={id} onClick={event => {
      const rect = event.currentTarget.getBoundingClientRect();
      const width = Math.min(420, window.innerWidth - 32);
      const top = Math.max(16, Math.min(rect.bottom + 8, window.innerHeight - 320));
      setPosition({ width, left: Math.max(16, Math.min(rect.right - width, window.innerWidth - width - 16)), top, maxHeight: window.innerHeight - top - 16 });
    }}><Info size={13} aria-hidden="true"/>Why this?</button>
    <div ref={popup} id={id} popover="auto" role="dialog" aria-label="Why this was selected" className="recommendation-popover" style={position} onToggle={event => setOpen(event.newState === "open")}>
      <div className="reason-heading"><h4>Why this was selected</h4><button className="icon-button" popoverTarget={id} popoverTargetAction="hide" aria-label="Close explanation" autoFocus><X size={16}/></button></div>
      {overview}
      {rationale?.evidence.map((evidence, index) => <p key={`${evidence.itemId}-${index}`}>
        {evidence.itemId === item.id ? <span>This source</span> : <Link className="text-link" href={`/items/${evidence.itemId}`}>{evidence.title}</Link>}
        {evidence.match ? <> · {evidence.field === "user-tag" ? "your tag" : evidence.field === "source-text" ? "source text" : evidence.field}: {evidence.match}</> : evidence.concept ? <> · {evidence.concept}</> : null}
        {evidence.relationship === "saved" ? " · saved when selected" : evidence.relationship === "read" ? " · marked read when selected" : evidence.relationship === "user-trail" ? " · chosen for a trail" : evidence.relationship === "positive-feedback" ? " · More like this feedback" : null}
      </p>)}
    </div>
  </div>;
  return <div className="recommendation-reason">
    {overview}
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
