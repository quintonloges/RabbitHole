import { publicationDate } from "@/lib/api";
import { contentAccess, contentType, itemHeading, pageLocation, sourceExcerpt, sourceLabel } from "@/lib/item-display";
import type { Item } from "@/lib/types";

export function ItemFormat({ item }: { item: Item }) {
  return <span className="item-format">({contentType(item)})</span>;
}

export function ItemByline({ item }: { item: Item }) {
  const author = item.author || item.document?.author;
  const location = pageLocation(item);
  const access = contentAccess(item);
  return <div className="item-byline">
    {itemHeading(item) === item.title && <span className="item-source">{item.document ? "From " : ""}{sourceLabel(item)}</span>}
    {author && <span>by {author}</span>}
    {!item.document && <span>{item.publishedAt ? "Published " : ""}{publicationDate(item.publishedAt)}</span>}
    {location && <span>{location}</span>}
    {access && <span>{access}</span>}
    {item.document?.isSuperseded && <span>Previous extraction</span>}
  </div>;
}

export function ItemExcerpt({ item }: { item: Item }) {
  const excerpt = sourceExcerpt(item);
  return excerpt ? <p className="item-excerpt" aria-label={item.document ? "Book excerpt" : item.textProvenance === "provider-abstract" ? "Abstract" : "Source excerpt"}>{excerpt}</p> : null;
}
