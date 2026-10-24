import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { PublicApiError, publicApi, publicOrigin, indexingEnabled } from "@/lib/public-api";
import type { PublicItem } from "@/lib/public-types";
import { PublicByline, PublicItems } from "@/components/public-items";
import { PublicMedia } from "@/components/public-media";

async function load(id: string) {
  try { return await publicApi<PublicItem>(`/items/${encodeURIComponent(id)}`); }
  catch (error) { if (error instanceof PublicApiError && error.status === 404) notFound(); throw error; }
}
export async function generateMetadata({ params }: { params: Promise<{ id: string }> }): Promise<Metadata> {
  const { id } = await params;
  const item = await load(id);
  return { title: `${item.title} · RabbitHole`, description: item.excerpt?.slice(0, 160) || `${item.type} from ${item.sourceName || item.provider}.`, alternates: { canonical: `${publicOrigin()}/items/${encodeURIComponent(item.id)}` }, robots: { index: indexingEnabled(), follow: true } };
}
export default async function PublicItemPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params; const item = await load(id);
  const related = await publicApi<PublicItem[]>(`/items/${encodeURIComponent(item.id)}/related`).catch(() => null);
  return <div className="reader item-reader"><Link prefetch={false} href="/" className="reader-back">← Catalog</Link><header className="reader-header"><div className="reader-title"><h1>{item.title}</h1><span className="item-format">{item.type.replaceAll("-", " ")}</span></div><PublicByline item={item}/><div className="reader-primary-actions"><a className="reader-text-button" href={item.canonicalUrl} target="_blank" rel="noopener noreferrer">Original source ↗</a></div><div className="chip-row">{item.topics.map(topic => <Link prefetch={false} href={`/?topicId=${encodeURIComponent(topic.id)}`} className="pill" key={topic.id}>{topic.name}</Link>)}</div></header><article className="reader-content" aria-label="Source preview">{item.embedUrl && <PublicMedia key={item.id} url={item.embedUrl} title={item.title}/ >}{item.excerpt && <p className="reader-lede">{item.excerpt}</p>}<div className="reader-content-notice"><p>{item.excerpt ? "This is a source preview. Continue at the original source to read more." : "Read or listen at the original source."}</p><a className="btn" href={item.canonicalUrl} target="_blank" rel="noopener noreferrer">Open original source ↗</a></div></article><section className="reader-section" aria-label="Related public items"><h2 className="section-title">Follow a connection</h2><p className="subtle">Related by topics and source metadata.</p>{related?.length ? <PublicItems items={related}/> : <p className="muted">{related === null ? "Related items are temporarily unavailable." : "No related items in the public catalog yet."} <Link prefetch={false} className="text-link" href="/search">Search the catalog →</Link></p>}</section></div>;
}
