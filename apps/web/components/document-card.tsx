"use client";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { ArrowRight, Plus } from "lucide-react";
import { api, json, readableDate } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { Book, BookSection } from "@/lib/types";
import { Notice } from "./ui";
import { BookSections } from "./book-sections";

export function DocumentCard({ book, refreshVersion, onChanged }: { book: Book; refreshVersion: number; onChanged: () => Promise<void> }) {
  const [expanded, setExpanded] = useState(false);
  const sections = useApi<BookSection[]>(expanded ? `/library/books/${book.id}/sections` : null);
  const seenVersion = useRef(refreshVersion);
  const { reload: reloadSections } = sections;
  useEffect(() => {
    if (seenVersion.current !== refreshVersion) {
      seenVersion.current = refreshVersion;
      void reloadSections();
    }
  }, [refreshVersion, reloadSections]);
  const [editing, setEditing] = useState(false);
  const [title, setTitle] = useState(book.title);
  const [author, setAuthor] = useState(book.author || "");
  const [rangeTitle, setRangeTitle] = useState("");
  const [startPage, setStartPage] = useState(1);
  const [endPage, setEndPage] = useState(1);
  const [busy, setBusy] = useState<"details" | "range" | "extraction" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const isPdf = book.fileName.toLowerCase().endsWith(".pdf");
  const ready = book.status === "ready";
  const validRange = rangeTitle.trim() && Number.isInteger(startPage) && Number.isInteger(endPage) && startPage >= 1 && endPage >= startPage && (!book.pageCount || endPage <= book.pageCount);

  async function saveDetails() {
    if (!title.trim() || busy) return;
    setBusy("details"); setError(null);
    try {
      await api(`/library/books/${book.id}`, json("PATCH", { title: title.trim(), author: author.trim() || null }));
      setEditing(false); await onChanged();
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Could not save book details."); }
    finally { setBusy(null); }
  }
  async function addRange() {
    if (!validRange || busy) return;
    setBusy("range"); setError(null);
    try {
      await api(`/library/books/${book.id}/sections`, json("POST", { title: rangeTitle.trim(), startPage, endPage }));
      setRangeTitle(""); await reloadSections(); await onChanged();
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Could not add range."); }
    finally { setBusy(null); }
  }
  async function rebuild() {
    if (busy || sections.loading) return;
    setBusy("extraction"); setError(null); sections.setError(null);
    try {
      const result = await api<{ document: Book; sections: BookSection[] }>(`/library/books/${book.id}/retry`, json("POST"));
      sections.setData(result.sections); sections.setError(null);
      await onChanged();
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Could not retry document extraction."); await onChanged(); }
    finally { setBusy(null); }
  }

  return <div className="panel panel-pad document-card">
    <div className="row-item" style={{ paddingTop: 0 }}><div><span className="eyebrow">{book.fileName.split(".").pop()?.toUpperCase()} · {book.status}</span><h3 className="serif" style={{ fontSize: 23, margin: "9px 0 4px" }}>{book.title}</h3><span className="subtle">{book.author || "Author unknown"} · Added {readableDate(book.createdAt)}{book.pageCount ? ` · ${book.pageCount} ${book.pageCount === 1 ? "page" : "pages"}` : ""}</span></div><button className="btn small" aria-expanded={expanded} disabled={busy !== null} onClick={() => setExpanded(!expanded)}>{expanded ? "Close" : "Sections"} <ArrowRight size={13}/></button></div>
    {book.error && <p className="error">{book.error}</p>}
    <Notice error={error}/>
    <div className="action-row" style={{ marginTop: 12 }}>
      <Link href={`/api/library/books/${book.id}/file`} target="_blank" className="btn small">Open original file</Link>
      <button className="btn small" disabled={busy !== null || sections.loading} onClick={() => void rebuild()}>{busy === "extraction" ? ready ? "Rebuilding sections…" : "Retrying extraction…" : ready ? "Rebuild sections" : "Retry extraction"}</button>
      <button className="btn small" disabled={busy !== null} onClick={() => { setEditing(!editing); setTitle(book.title); setAuthor(book.author || ""); }}>Edit details</button>
    </div>
    {editing && <fieldset className="stack document-fields" disabled={busy !== null}><label className="field">Book title<input value={title} onChange={event => setTitle(event.target.value)}/></label><label className="field">Author<input value={author} onChange={event => setAuthor(event.target.value)}/></label><button className="btn primary" disabled={!title.trim()} onClick={() => void saveDetails()}>{busy === "details" ? "Saving…" : "Save book details"}</button></fieldset>}
    {expanded && <div className="stack" style={{ marginTop: 18 }}>
      {isPdf && <details className="section-group"><summary>Mark a page range</summary><fieldset className="stack document-fields" disabled={busy !== null}>
        <div className="form-grid"><label className="field">Title<input value={rangeTitle} onChange={event => setRangeTitle(event.target.value)} placeholder="Chapter or section"/></label><div className="form-grid"><label className="field">From<input type="number" min="1" max={book.pageCount || undefined} value={startPage} onChange={event => setStartPage(Number(event.target.value))}/></label><label className="field">To<input type="number" min={startPage} max={book.pageCount || undefined} value={endPage} onChange={event => setEndPage(Number(event.target.value))}/></label></div></div>
        <button className="btn small" disabled={!validRange} onClick={() => void addRange()}><Plus size={13}/>{busy === "range" ? "Adding range…" : "Add range"}</button>
      </fieldset></details>}
      <Notice error={sections.error} onRetry={() => void reloadSections()}/>
      {sections.loading && !sections.data ? <p className="subtle" role="status">Loading sections…</p> : sections.data && <BookSections sections={sections.data} isPdf={isPdf}/>}
    </div>}
  </div>;
}
