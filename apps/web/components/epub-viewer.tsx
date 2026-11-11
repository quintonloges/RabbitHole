"use client";

import { useEffect, useRef, useState } from "react";
import { ChevronLeft, ChevronRight, Moon, ZoomIn, ZoomOut } from "lucide-react";
import { api } from "@/lib/api";
import "./epub-viewer.css";

type Chapter = { title: string; href: string };
type Index = { chapters: Chapter[]; entryLocations: Record<string, string> };
type Location = { href: string; scroll?: number };
type Props = { documentId: string; itemId: string; title: string };
const appearanceKey = "rabbithole-epub-appearance";

export default function EpubViewer({ documentId, itemId, title }: Props) {
  const frame = useRef<HTMLIFrameElement>(null);
  const detach = useRef<(() => void) | null>(null);
  const deadline = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [index, setIndex] = useState<Index | null>(null);
  const [location, setLocation] = useState<Location | null>(null);
  const [loading, setLoading] = useState(true);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [zoom, setZoom] = useState(1);
  const [dark, setDark] = useState(false);
  const storageKey = `rabbithole-epub-position:${itemId}`;
  const assetRoot = `/api/library/books/${documentId}/epub/assets/`;
  const current = index?.chapters.findIndex(chapter => chapter.href === location?.href) ?? -1;
  const chapterIndex = current >= 0 ? current : index?.chapters.findIndex(chapter => chapter.href.split("#")[0] === location?.href.split("#")[0]) ?? -1;

  useEffect(() => {
    let stored: string | null = null;
    try { stored = localStorage.getItem(appearanceKey); } catch { /* Optional preference. */ }
    setDark(stored ? stored === "dark" : getComputedStyle(document.documentElement).colorScheme === "dark");
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    detach.current?.(); detach.current = null;
    setLoading(true); setLoaded(false); setError(null); setLocation(null);
    void api<Index>(`/library/books/${documentId}/epub`, { signal: controller.signal }).then(book => {
      if (!active) return;
      if (!book.chapters.length) throw new Error("No supported chapters are available. Use Extracted text or download the original EPUB.");
      let next: Location = { href: book.entryLocations[itemId] || book.chapters[0].href };
      try {
        const saved = JSON.parse(localStorage.getItem(storageKey) || "null");
        if (saved && typeof saved.href === "string" && book.chapters.some(chapter => chapter.href.split("#")[0] === saved.href.split("#")[0]) && Number.isFinite(saved.scroll) && saved.scroll >= 0)
          next = { href: saved.href, scroll: saved.scroll };
      } catch { /* Start at the selected section when no position is available. */ }
      setIndex(book); setLocation(next);
    }).catch(cause => {
      if (active) { setError(cause instanceof Error ? cause.message : "The EPUB could not be opened."); setLoading(false); }
    });
    return () => { active = false; controller.abort(); detach.current?.(); detach.current = null; };
  }, [documentId, itemId, storageKey, attempt]);

  useEffect(() => {
    if (!location) return;
    deadline.current = setTimeout(() => { setError("This chapter took too long to load. Try again or use Extracted text."); setLoading(false); }, 20000);
    return () => { if (deadline.current) clearTimeout(deadline.current); };
  }, [location]);

  useEffect(() => {
    if (loaded) frame.current?.contentDocument?.documentElement.style.setProperty("zoom", String(zoom));
  }, [zoom, loaded]);

  function navigate(href: string) {
    if (href === location?.href) return;
    detach.current?.(); detach.current = null;
    setLoading(true); setLoaded(false); setError(null); setLocation({ href });
  }

  function frameLoaded() {
    if (deadline.current) clearTimeout(deadline.current);
    const element = frame.current;
    const document = element?.contentDocument;
    const view = element?.contentWindow;
    if (!document || !view || document.documentElement.getAttribute("data-epub-document") !== "true" || !location) {
      setError("This chapter could not be displayed. Try again, use Extracted text, or download the original EPUB."); setLoading(false); return;
    }
    detach.current?.();
    document.documentElement.style.setProperty("zoom", String(zoom));
    if (location.scroll !== undefined) view.scrollTo(0, location.scroll);
    setLoading(false); setLoaded(true);
    const href = location.href;
    // Detached iframe windows report zero; retain the last live offset for cleanup.
    let scroll = view.scrollY;
    let pending: ReturnType<typeof setTimeout> | null = null;
    function save() {
      if (element?.isConnected) scroll = view!.scrollY;
      try { localStorage.setItem(storageKey, JSON.stringify({ href, scroll })); } catch { /* Viewing remains usable without storage. */ }
    }
    function scrolled() {
      if (!element?.isConnected) return;
      scroll = view!.scrollY;
      if (pending) clearTimeout(pending);
      pending = setTimeout(save, 200);
    }
    function clicked(event: MouseEvent) {
      const target = event.target as Element | null;
      const link = typeof target?.closest === "function" ? target.closest("a") : null;
      if (!link) return;
      event.preventDefault();
      const external = link.getAttribute("data-epub-external");
      if (external) {
        try { const url = new URL(external); if (["https:", "http:"].includes(url.protocol) && !url.username && !url.password) window.open(url.href, "_blank", "noopener,noreferrer"); } catch { /* Invalid links stay inert. */ }
        return;
      }
      try {
        const url = new URL(link.getAttribute("href") || "", window.location.origin + href);
        if (url.origin === window.location.origin && url.pathname.startsWith(assetRoot) && index?.chapters.some(chapter => chapter.href.split("#")[0] === url.pathname))
          navigate(url.pathname + url.hash);
      } catch { /* Unsupported navigation stays inside the reader. */ }
    }
    view.addEventListener("scroll", scrolled, { passive: true });
    document.addEventListener("click", clicked);
    save();
    detach.current = () => { if (pending) clearTimeout(pending); save(); view.removeEventListener("scroll", scrolled); document.removeEventListener("click", clicked); };
  }

  return <section className="epub-viewer" aria-label={`EPUB viewer: ${title}`}>
    <div className="epub-toolbar" aria-label="EPUB controls">
      <div className="epub-chapter-controls">
        <button className="icon-button" aria-label="Previous EPUB chapter" disabled={!index || chapterIndex <= 0 || !!error} onClick={() => navigate(index!.chapters[chapterIndex - 1].href)}><ChevronLeft size={18}/></button>
        <label>Chapter<select aria-label="EPUB chapter" value={chapterIndex < 0 ? "" : index?.chapters[chapterIndex].href} disabled={!index || !!error} onChange={event => navigate(event.target.value)}>
          {!index && <option value="">Loading…</option>}
          {index?.chapters.map(chapter => <option key={chapter.href} value={chapter.href}>{chapter.title}</option>)}
        </select></label>
        <button className="icon-button" aria-label="Next EPUB chapter" disabled={!index || chapterIndex < 0 || chapterIndex >= index.chapters.length - 1 || !!error} onClick={() => navigate(index!.chapters[chapterIndex + 1].href)}><ChevronRight size={18}/></button>
      </div>
      <div className="epub-zoom-controls">
        <button className="icon-button" aria-label="Zoom out EPUB" disabled={zoom <= 0.75} onClick={() => setZoom(value => Math.max(0.75, value - 0.25))}><ZoomOut size={17}/></button>
        <button className="reader-text-button" title="Reset EPUB zoom" onClick={() => setZoom(1)}>{Math.round(zoom * 100)}%</button>
        <button className="icon-button" aria-label="Zoom in EPUB" disabled={zoom >= 2} onClick={() => setZoom(value => Math.min(2, value + 0.25))}><ZoomIn size={17}/></button>
      </div>
      <button className="btn small epub-dark-toggle" aria-pressed={dark} title="Dark pages also change image colors" onClick={() => {
        const next = !dark; setDark(next);
        try { localStorage.setItem(appearanceKey, next ? "dark" : "original"); } catch { /* Optional preference. */ }
      }}><Moon size={15}/>Dark pages</button>
    </div>
    {error ? <div className="epub-error"><p role="alert">{error}</p><div className="action-row"><button className="btn small" onClick={() => setAttempt(value => value + 1)}>Retry EPUB</button><a className="btn small" href={`/api/library/books/${documentId}/file`} target="_blank" rel="noopener noreferrer">Download original EPUB</a></div></div>
      : <div className="epub-frame-wrap" aria-busy={loading} data-dark={dark}>
        {loading && <p className="epub-loading" role="status">Loading original EPUB…</p>}
        {location && <iframe key={location.href} ref={frame} title={`EPUB chapter: ${index?.chapters[chapterIndex]?.title || title}`} src={location.href} sandbox="allow-same-origin" referrerPolicy="no-referrer" onLoad={frameLoaded} onError={() => { setError("The chapter is unavailable. Try again or use Extracted text."); setLoading(false); }}/>}
      </div>}
    <div className="epub-caption"><span role="status">{loaded && !error && index ? `Section ${chapterIndex + 1} of ${index.chapters.length}` : "Original EPUB"}</span><span>{dark ? "Dark pages change image colors." : "Original book formatting."}</span></div>
  </section>;
}
