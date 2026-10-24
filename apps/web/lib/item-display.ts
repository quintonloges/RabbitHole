import { safeExternal } from "./api";
import type { Item } from "./types";

export function contentType(item: Item) {
  if (item.document) {
    const { format, sectionType } = item.document;
    return sectionType === "document" ? format : `${format} ${sectionType}`;
  }
  return ({ paper: "Research paper", article: "Article", video: "Video", audio: "Audio", music: "Music", book: "Book", "book-section": "Book section" } as Record<string, string>)[item.kind] || item.kind.replaceAll("-", " ");
}

export function itemHeading(item: Item) {
  if (item.document && /^Text from .+\.x?html?$/i.test(item.title)) {
    return `${item.document.title} — ${item.document.sectionNumber ? `section ${item.document.sectionNumber}` : "untitled section"}`;
  }
  return item.document?.sectionType === "page" && /^Page \d+$/i.test(item.title)
    ? `${item.document.title} — ${item.title.toLowerCase()}` : item.title;
}

export function pageLocation(item: Item) {
  const doc = item.document;
  if (!doc?.startPage) return null;
  return doc.endPage && doc.endPage !== doc.startPage
    ? `Pages ${doc.startPage}–${doc.endPage}`
    : `Page ${doc.startPage}${doc.pageCount ? ` of ${doc.pageCount}` : ""}`;
}

export function sourceLabel(item: Item) {
  if (item.document) return item.document.title;
  const name = item.sourceName?.trim();
  if (name && name !== item.provider) return name;
  const providers: Record<string, string> = { youtube: "YouTube", soundcloud: "SoundCloud", arxiv: "arXiv", openalex: "OpenAlex", "local-upload": "Your library" };
  if (providers[item.provider]) return providers[item.provider];
  const url = safeExternal(item.canonicalUrl);
  return url ? new URL(url).hostname.replace(/^www\./, "") : name || "Personal import";
}

export function safeEmbed(item: Item) {
  try {
    const url = new URL(item.embedUrl || "");
    const host = url.hostname.toLowerCase();
    if (url.protocol !== "https:") return null;
    if ((host === "www.youtube-nocookie.com" || host === "www.youtube.com") && /^\/embed\/[A-Za-z0-9_-]{11}$/.test(url.pathname)) return url.href;
    if (host === "w.soundcloud.com" && url.pathname === "/player/") return url.href;
  } catch { /* Unavailable and invalid players use the original source link. */ }
  return null;
}

export function contentAccess(item: Item) {
  // A list response deliberately omits Body. Only the server's availability flag (or actual text) is evidence.
  const hasText = item.hasText ?? Boolean(item.body?.trim());
  if (item.document) return hasText ? null : "No extracted text";
  if (safeEmbed(item)) return null;
  if (hasText) return item.textProvenance === "provider-abstract" ? "Abstract only" : null;
  return item.excerpt?.trim() ? "Excerpt only" : "Original link only";
}

export function sourceExcerpt(item: Item) {
  const text = (item.excerpt || "").replace(/\s+/g, " ").trim();
  const title = item.title.replace(/\s+/g, " ").trim();
  return text.startsWith(title + " ") ? text.slice(title.length).trim() : text;
}
