import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, ArrowUpRight } from "lucide-react";
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
  return <div className="public-item-page">
    <Link prefetch={false} href="/" className="public-item-back"><ArrowLeft size={14} aria-hidden="true"/>Catalog</Link>
    <header className="public-item-header">
      <h1>{item.title}</h1>
      <div className="public-item-meta"><PublicByline item={item}/><span>{item.type.replaceAll("-", " ")}</span></div>
      {item.topics.length > 0 && <nav className="public-item-topics" aria-label="Item topics">
        {item.topics.map(topic => <Link prefetch={false} href={`/?topicId=${encodeURIComponent(topic.id)}`} key={topic.id}>{topic.name}</Link>)}
      </nav>}
    </header>
    <article className="public-item-content" aria-label="Source preview">
      {item.embedUrl && <PublicMedia key={item.id} url={item.embedUrl} title={item.title}/>}
      {item.excerpt && <p className="public-item-excerpt">{item.excerpt}</p>}
      <div className="public-item-source">
        <a className="public-source-link" href={item.canonicalUrl} target="_blank" rel="noopener noreferrer">Open original source<ArrowUpRight size={15} aria-hidden="true"/></a>
        <p>{item.excerpt ? "This is a source preview. Continue at the original source to read more." : "Read or listen at the original source."}</p>
      </div>
    </article>
    <section className="public-item-related" aria-label="Related public items">
      <div className="public-related-heading"><h2>Follow a connection</h2><p>Related by topics and source metadata.</p></div>
      {related?.length ? <PublicItems items={related}/> : <p className="public-related-empty">
        {related === null ? "Related items are temporarily unavailable." : "No related items in the public catalog yet."} <Link prefetch={false} className="text-link" href="/search">Search the catalog →</Link>
      </p>}
    </section>
  </div>;
}
