import Link from "next/link";
import type { PublicItem } from "@/lib/public-types";
import { datedTitle } from "@/lib/publication";

export function PublicByline({ item, compact = false }: { item: PublicItem; compact?: boolean }) {
  const date = item.publishedAt ? new Date(item.publishedAt).toLocaleDateString("en-US", { month: compact ? "short" : "long", day: "numeric", year: "numeric", timeZone: "UTC" }) : "Date unknown";
  return <div className="item-byline">
    <span className="item-source">{item.sourceName || item.provider}</span>
    {item.author && <span>{item.author}</span>}
    <span>{item.publishedAt ? <time dateTime={item.publishedAt}>Published {date}</time> : date}</span>
    {compact && <span>{item.type.replaceAll("-", " ")}</span>}
  </div>;
}

function sourceHost(url: string) {
  try { return new URL(url).hostname.replace(/^www\./, ""); }
  catch { return null; }
}

export function PublicItems({ items, start = 1 }: { items: PublicItem[]; start?: number }) {
  return <ol className="article-list public-article-list" start={start}>{items.map((item, index) => <li key={item.id}>
    <article className="story-row">
      <span className="story-number" aria-hidden="true">{start + index}.</span>
      <div className="story-content">
        <div className="story-heading">
          <h3><a href={item.canonicalUrl} target="_blank" rel="noopener noreferrer">{datedTitle(item.title, item.publishedAt)}</a></h3>
          <span className="story-domain">{sourceHost(item.canonicalUrl)}</span>
        </div>
        {item.excerpt && <p className="item-excerpt">{item.excerpt}</p>}
        <div className="story-footer">
          <PublicByline item={item} compact/>
          <div className="story-tools">
            <Link prefetch={false} className="story-action story-explore" href={`/items/${item.id}`}>Go down the rabbit hole ↘</Link>
            {item.topics.map(topic => <Link prefetch={false} className="story-action" key={topic.id} href={`/?topicId=${encodeURIComponent(topic.id)}`}>{topic.name}</Link>)}
          </div>
        </div>
      </div>
    </article>
  </li>)}</ol>;
}
