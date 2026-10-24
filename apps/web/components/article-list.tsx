"use client";

import Link from "next/link";
import { ArrowUpRight } from "lucide-react";
import { useEffect, useState } from "react";
import type { Item } from "@/lib/types";
import { RecommendationExplanation } from "./recommendation-explanation";
import { itemHeading } from "@/lib/item-display";
import { ItemByline, ItemExcerpt, ItemFormat } from "./item-summary";
import { ExternalLink, StateActions } from "./ui";

export function ArticleList({ items, onChange, label, start = 1 }: { items: Item[]; onChange?: (item: Item) => void; label: string; start?: number }) {
  return <ol className="article-list" aria-label={label} start={start}>
    {items.map((item, index) => <li key={item.id}>
      <ArticleRow item={item} number={start + index} onChange={onChange}/>
    </li>)}
  </ol>;
}

function ArticleRow({ item, number, onChange }: { item: Item; number: number; onChange?: (item: Item) => void }) {
  const [current, setCurrent] = useState(item);
  useEffect(() => { setCurrent(item); }, [item]);
  function update(next: Item) { setCurrent(next); onChange?.(next); }

  return <article className={`story-row${current.state.read ? " is-read" : ""}`}>
    <span className="story-number" aria-hidden="true">{number}.</span>
    <div className="story-content">
      <div className="story-heading">
        <h3><Link href={`/items/${item.id}`}>{itemHeading(item)}</Link></h3>
        <ItemFormat item={item}/>
        {current.state.read && <span className="story-read">Read</span>}
      </div>
      <ItemByline item={item}/>
      <ItemExcerpt item={item}/>
      <div className="story-tools">
        {item.documentId ? <Link className="story-action" href={`/api/library/books/${item.documentId}/file`} target="_blank">Original file<ArrowUpRight size={12} aria-hidden="true"/></Link> : <ExternalLink url={item.canonicalUrl} className="story-action">Original source<ArrowUpRight size={12} aria-hidden="true"/></ExternalLink>}
        <StateActions item={current} onChange={update} inline/>
        <RecommendationExplanation item={item} compact/>
      </div>
    </div>
  </article>;
}
