import Link from "next/link";
import type { PublicItem } from "@/lib/public-types";

export function PublicByline({ item }: { item: PublicItem }) {
  const date = item.publishedAt ? new Date(item.publishedAt).toLocaleDateString("en-US", { month: "long", day: "numeric", year: "numeric", timeZone: "UTC" }) : "Date unknown";
  return <div className="item-byline"><span>{item.sourceName || item.provider}</span>{item.author && <span>{item.author}</span>}<span>{date}</span></div>;
}
export function PublicItems({ items, start = 1 }: { items: PublicItem[]; start?: number }) {
  return <ol className="article-list" start={start}>{items.map((item, index) => <li key={item.id}><article className="story-row"><span className="story-number" aria-hidden="true">{start + index}</span><div className="story-content"><div className="story-heading"><h3><a href={item.canonicalUrl} target="_blank" rel="noopener noreferrer">{item.title}</a></h3><span className="item-format">{item.type.replaceAll("-", " ")}</span></div><PublicByline item={item}/>{item.excerpt && <p className="item-excerpt">{item.excerpt}</p>}<div className="story-tools"><Link prefetch={false} className="story-action" href={`/items/${item.id}`}>Go down the rabbit hole ↘</Link>{item.topics.map(topic => <Link prefetch={false} className="story-action" key={topic.id} href={`/?topicId=${encodeURIComponent(topic.id)}`}>{topic.name}</Link>)}</div></div></article></li>)}</ol>;
}
