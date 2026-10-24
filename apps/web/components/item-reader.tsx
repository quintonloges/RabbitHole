"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { ArrowLeft, ArrowRight, ArrowUpRight, GitBranch, Highlighter, MessageCircle, X } from "lucide-react";
import { api, json, readableDate } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import { itemHeading, safeEmbed, sourceExcerpt, sourceLabel } from "@/lib/item-display";
import type { BookSection, HoleSummary, Item, ItemState, Note, Topic } from "@/lib/types";
import { ExternalLink, Loading, Notice, StateActions } from "./ui";
import { ItemByline, ItemFormat } from "./item-summary";
import { ArticleList } from "./article-list";
import { ConceptEditor } from "./concept-editor";
import { PaperProvenance } from "./paper-provenance";
import { ReaderNavigation } from "./reader-navigation";

type Tool = "notes" | "explore" | "details";
const message = (error: unknown, fallback: string) => error instanceof Error ? error.message : fallback;

export function ItemReader({ id }: { id: string }) {
  const router = useRouter();
  const itemQuery = useApi<Item>(`/items/${id}`);
  const item = itemQuery.data;
  const sections = useApi<BookSection[]>(item?.documentId ? `/library/books/${item.documentId}/sections` : null);
  const topics = useApi<Topic[]>("/topics");
  const aiStatus = useApi<{ configured?: boolean; enabled?: boolean; privacy?: string }>("/ai/status");
  const [tool, setTool] = useState<Tool>("notes");
  const toolsRef = useRef<HTMLElement>(null);
  const noteInput = useRef<HTMLTextAreaElement>(null);
  const sourceText = useRef<HTMLDivElement>(null);
  const active = useRef(true);
  const opened = useRef(false);
  const draftKey = `rabbithole-note-draft:${id}`;
  const [noteBody, setNoteBody] = useState("");
  const [quote, setQuote] = useState("");
  const [noteBusy, setNoteBusy] = useState(false);
  const [noteError, setNoteError] = useState<string | null>(null);
  const [noteMessage, setNoteMessage] = useState<string | null>(null);
  const [selection, setSelection] = useState("");
  const [highlightBusy, setHighlightBusy] = useState(false);
  const [highlightError, setHighlightError] = useState<string | null>(null);
  const [highlightMessage, setHighlightMessage] = useState<string | null>(null);
  const [embedConsent, setEmbedConsent] = useState(false);
  const [preservePdfLines, setPreservePdfLines] = useState(false);
  const [holeBusy, setHoleBusy] = useState(false);
  const [holeError, setHoleError] = useState<string | null>(null);
  const [aiText, setAiText] = useState<string | null>(null);
  const [aiEvidence, setAiEvidence] = useState<string[]>([]);
  const [aiBusy, setAiBusy] = useState(false);
  const [aiError, setAiError] = useState<string | null>(null);
  const [lesson, setLesson] = useState<{ title: string; items: Item[]; question: string; provenance: string } | null>(null);
  const [trailBusy, setTrailBusy] = useState(false);
  const [trailError, setTrailError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);
  const [title, setTitle] = useState("");
  const [author, setAuthor] = useState("");
  const [editingTopics, setEditingTopics] = useState(false);
  const [selectedTopics, setSelectedTopics] = useState<string[]>([]);
  const [detailsBusy, setDetailsBusy] = useState(false);
  const [detailsError, setDetailsError] = useState<string | null>(null);
  const [progressError, setProgressError] = useState<string | null>(null);
  const [progressBusy, setProgressBusy] = useState(false);
  const savedProgress = useRef<number | null>(null);
  const progressPending = useRef(false);

  useEffect(() => {
    active.current = true;
    try {
      const draft = JSON.parse(sessionStorage.getItem(draftKey) || "null");
      if (draft && typeof draft.body === "string" && typeof draft.quote === "string") { setNoteBody(draft.body); setQuote(draft.quote); }
    } catch { /* Draft storage is optional; saved notes always come from the API. */ }
    return () => { active.current = false; };
  }, [draftKey]);
  useEffect(() => {
    if (!item) return;
    if (savedProgress.current === null) savedProgress.current = item.state.progress || 0;
    if (!opened.current) { opened.current = true; void api(`/items/${id}/state`, json("PATCH", { opened: true })).catch(() => {}); }
  }, [id, item]);
  useEffect(() => {
    if (!item?.documentId) return;
    try { setPreservePdfLines(sessionStorage.getItem(`rabbithole-pdf-lines:${item.documentId}`) === "original"); } catch { /* Optional display preference. */ }
  }, [item?.documentId]);
  useEffect(() => {
    function changed() {
      const selected = window.getSelection();
      const inside = selected && !selected.isCollapsed && sourceText.current?.contains(selected.anchorNode) && sourceText.current?.contains(selected.focusNode);
      setSelection(inside ? selected.toString().trim() : "");
    }
    document.addEventListener("selectionchange", changed);
    return () => document.removeEventListener("selectionchange", changed);
  }, []);

  function updateDraft(body: string, passage: string) {
    setNoteBody(body); setQuote(passage);
    try {
      if (body || passage) sessionStorage.setItem(draftKey, JSON.stringify({ body, quote: passage }));
      else sessionStorage.removeItem(draftKey);
    } catch { /* The editor still works when browser storage is unavailable. */ }
  }
  function openTool(next: Tool, focusNote = false) {
    setTool(next);
    requestAnimationFrame(() => {
      toolsRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
      if (focusNote) noteInput.current?.focus({ preventScroll: true });
    });
  }
  async function saveNote() {
    if (noteBusy || !noteBody.trim()) return;
    setNoteBusy(true); setNoteError(null); setNoteMessage(null);
    const submitted = JSON.stringify({ body: noteBody, quote });
    try {
      const note = await api<Note>(`/items/${id}/notes`, json("POST", { body: noteBody.trim(), quote: quote || undefined }));
      // A completed save may outlive navigation. Remove only the draft that was actually submitted.
      try { if (sessionStorage.getItem(draftKey) === submitted) sessionStorage.removeItem(draftKey); } catch { /* Optional draft storage. */ }
      if (!active.current) return;
      itemQuery.setData(old => old ? { ...old, notes: [...(old.notes || []), note] } : old);
      updateDraft("", ""); setNoteMessage("Note saved.");
    } catch (error) { if (active.current) setNoteError(message(error, "Could not save note.")); }
    finally { if (active.current) setNoteBusy(false); }
  }
  async function captureHighlight() {
    if (!selection || highlightBusy) return;
    if (selection.length > 4000) { setHighlightError("Choose a shorter passage (up to 4,000 characters)."); return; }
    setHighlightBusy(true); setHighlightError(null); setHighlightMessage(null);
    try {
      const note = await api<Note>(`/items/${id}/notes`, json("POST", { body: "Highlighted passage", quote: selection }));
      if (!active.current) return;
      itemQuery.setData(old => old ? { ...old, notes: [...(old.notes || []), note] } : old);
      setHighlightMessage("Highlight saved."); window.getSelection()?.removeAllRanges(); setSelection("");
    } catch (error) { if (active.current) setHighlightError(message(error, "Highlight could not be saved. Try again.")); }
    finally { if (active.current) setHighlightBusy(false); }
  }
  async function startHole() {
    if (holeBusy) return;
    setHoleBusy(true); setHoleError(null);
    try {
      const hole = await api<HoleSummary>("/holes", json("POST", { itemId: id, title: item?.title }));
      if (active.current) router.push(`/holes/${hole.id}`);
    } catch (error) { if (active.current) setHoleError(message(error, "Could not start rabbit hole.")); }
    finally { if (active.current) setHoleBusy(false); }
  }
  async function askAi(action: string) {
    setAiBusy(true); setAiError(null);
    try {
      const result = await api<{ text: string; evidenceItemIds: string[] }>(`/items/${id}/ai`, json("POST", { action }));
      if (active.current) { setAiText(result.text); setAiEvidence(result.evidenceItemIds); }
    } catch (error) { if (active.current) setAiError(message(error, "AI tools are unavailable right now.")); }
    finally { if (active.current) setAiBusy(false); }
  }
  async function takeDeeper() {
    setTrailBusy(true); setTrailError(null);
    try { const result = await api<NonNullable<typeof lesson>>(`/items/${id}/lesson`); if (active.current) setLesson(result); }
    catch (error) { if (active.current) setTrailError(message(error, "Could not find a reading trail.")); }
    finally { if (active.current) setTrailBusy(false); }
  }
  async function saveDetails(patch: object) {
    setDetailsBusy(true); setDetailsError(null);
    try {
      const updated = await api<Item>(`/items/${id}`, json("PATCH", patch));
      if (!active.current) return;
      itemQuery.setData(old => old ? { ...old, ...updated, notes: old.notes, related: old.related } : updated);
      setEditing(false); setEditingTopics(false);
    } catch (error) { if (active.current) setDetailsError(message(error, "Could not save details.")); }
    finally { if (active.current) setDetailsBusy(false); }
  }
  async function saveProgress(value: number) {
    if (progressPending.current || value === savedProgress.current) return;
    progressPending.current = true; setProgressBusy(true); setProgressError(null);
    try {
      await api<ItemState>(`/items/${id}/state`, json("PATCH", { progress: value }));
      if (active.current) savedProgress.current = value;
    } catch (error) {
      if (!active.current) return;
      itemQuery.setData(old => old ? { ...old, state: { ...old.state, progress: savedProgress.current || 0 } } : old);
      setProgressError(message(error, "Could not save progress."));
    } finally { progressPending.current = false; if (active.current) setProgressBusy(false); }
  }

  if (itemQuery.loading && !item) return <Loading/>;
  if (!item) return <><Notice error={itemQuery.error} onRetry={() => void itemQuery.reload()}/><Link href="/" className="btn"><ArrowLeft size={14}/>Discover</Link></>;
  const embed = safeEmbed(item);
  const body = item.body?.trim();
  // Reflow extraction whitespace only. Keep every word and existing paragraph boundary;
  // the stored text and original PDF remain untouched, including line-ending hyphens.
  const displayedBody = body && item.document?.format === "PDF" && !preservePdfLines
    ? body.replace(/\r\n?/g, "\n").split(/\n[ \t]*\n+/).map(paragraph => paragraph.replace(/\s+/g, " ").trim()).join("\n\n")
    : item.body;
  const excerpt = sourceExcerpt(item);
  const normalize = (text: string) => text.replace(/\s+/g, " ").trim().replace(/[…\s]+$/, "");
  const showExcerpt = excerpt && (!body || !normalize(body).includes(normalize(excerpt)));
  const originalFile = item.documentId ? `/api/library/books/${item.documentId}/file${item.document?.format === "PDF" && item.document.startPage ? `#page=${item.document.startPage}` : ""}` : null;
  const navigation = { sections: sections.data, currentItemId: item.id, isPdf: item.document ? item.document.format === "PDF" : undefined, loading: sections.loading, error: sections.error, onRetry: () => void sections.reload() };
  const notes = item.notes || [];

  return <div className="reader item-reader">
    <Link href="/" className="reader-back"><ArrowLeft size={14}/>Discover</Link>
    <header className="reader-header">
      <div className="reader-title"><h1>{itemHeading(item)}</h1><ItemFormat item={item}/></div>
      <ItemByline item={item}/>
      <div className="reader-primary-actions">
        <StateActions item={item} onChange={itemQuery.setData} reader/>
        {originalFile ? <Link className="reader-text-button" href={originalFile} target="_blank">Original file<ArrowUpRight size={14}/></Link> : <ExternalLink url={item.canonicalUrl} className="reader-text-button">Original source<ArrowUpRight size={14}/></ExternalLink>}
        <button className="reader-text-button" onClick={() => openTool("notes", true)}>Notes{notes.length ? ` (${notes.length})` : ""}</button>
      </div>
      <Notice error={itemQuery.error} onRetry={() => void itemQuery.reload()}/>
      {item.importWarning && <p className="reader-content-notice">{item.importWarning}</p>}
    </header>
    {item.documentId && <ReaderNavigation {...navigation}/>}

    <article className="reader-content" aria-label={embed ? "Media" : item.textProvenance === "provider-abstract" ? "Abstract" : "Source content"}>
      {embed && <div className="reader-player">
        {embedConsent ? <iframe title={`Embedded ${item.title}`} src={embed} loading="lazy" allow="accelerometer; autoplay; encrypted-media; gyroscope; picture-in-picture" referrerPolicy="strict-origin-when-cross-origin" sandbox="allow-scripts allow-same-origin allow-presentation" className={item.provider === "soundcloud" ? "audio-player" : "video-player"}/>
          : <div className="reader-player-consent"><h2>{item.kind === "video" ? "Watch" : "Listen"} on {sourceLabel(item)}</h2><p>Loading the player connects to {sourceLabel(item)}.</p><button className="btn primary" onClick={() => setEmbedConsent(true)}>Load player <ArrowRight size={14}/></button></div>}
      </div>}
      {body && item.textProvenance === "provider-abstract" && <h2 className="reader-content-title">Abstract</h2>}
      {showExcerpt && <p className="reader-lede">{excerpt}</p>}
      {body ? <div ref={sourceText} className="reader-body">{displayedBody}</div>
        : !embed && <div className="reader-content-notice"><p>{item.documentId ? "No text was extracted. Open the original file to read it." : excerpt ? "Only an excerpt is available here. Read the full text at the source." : "The text isn’t available here. Read it at the original source."}</p>{originalFile ? <Link className="btn small" href={originalFile} target="_blank">Open original file <ArrowUpRight size={14}/></Link> : <ExternalLink url={item.canonicalUrl} className="btn small">Open original source <ArrowUpRight size={14}/></ExternalLink>}</div>}
    </article>
    {item.documentId && <ReaderNavigation {...navigation} footer/>}

    <div className="reader-after-content"><button className="btn small" disabled={holeBusy} onClick={startHole}><GitBranch size={14}/>{holeBusy ? "Starting trail…" : "Follow the rabbit hole"}</button><button className="reader-text-button" onClick={() => openTool("notes", true)}><MessageCircle size={14}/>Add a note</button><button className="reader-text-button" onClick={() => openTool("explore")}>Related items <ArrowRight size={14}/></button></div>
    <Notice error={holeError}/>
    {item.documentId && <details className="reader-progress"><summary>Progress within this {item.document?.sectionType === "page" ? "page" : "item"} · {Math.round(item.state.progress * 100)}%</summary><label className="field">Reading progress<input type="range" min="0" max="100" disabled={progressBusy} value={Math.round(item.state.progress * 100)} onChange={event => itemQuery.setData(old => old ? { ...old, state: { ...old.state, progress: Number(event.target.value) / 100 } } : old)} onPointerUp={event => void saveProgress(Number(event.currentTarget.value) / 100)} onKeyUp={event => void saveProgress(Number(event.currentTarget.value) / 100)} onBlur={event => void saveProgress(Number(event.target.value) / 100)}/></label><Notice error={progressError}/></details>}

    <section ref={toolsRef} className="reader-tools" aria-label="Item tools">
      <div className="reader-tool-tabs" role="tablist" aria-label="Item tools">
        {(["notes", "explore", "details"] as Tool[]).map((name, index, tabs) => <button key={name} id={`tool-${name}`} role="tab" aria-selected={tool === name} aria-controls={`panel-${name}`} tabIndex={tool === name ? 0 : -1} onClick={() => setTool(name)} onKeyDown={event => {
          const next = event.key === "ArrowRight" ? tabs[(index + 1) % tabs.length] : event.key === "ArrowLeft" ? tabs[(index + tabs.length - 1) % tabs.length] : event.key === "Home" ? tabs[0] : event.key === "End" ? tabs[tabs.length - 1] : null;
          if (next) { event.preventDefault(); setTool(next); document.getElementById(`tool-${next}`)?.focus(); }
        }}>{name === "notes" ? `Notes${notes.length ? ` (${notes.length})` : ""}` : name === "explore" ? "Explore" : "Details"}</button>)}
      </div>

      <section id="panel-notes" role="tabpanel" aria-labelledby="tool-notes" hidden={tool !== "notes"} className="reader-tool-panel">
        <h2>Notes & highlights</h2>
        <div className="reader-note-editor">
          <label className="field">Your note<textarea ref={noteInput} value={noteBody} disabled={noteBusy} onChange={event => updateDraft(event.target.value, quote)} placeholder="Write a note…"/></label>
          {quote && <blockquote className="reader-note-quote">{quote}<button className="reader-text-button" disabled={noteBusy} onClick={() => updateDraft(noteBody, "")}>Remove quote</button></blockquote>}
          <div className="action-row"><button className="btn small" onClick={saveNote} disabled={noteBusy || !noteBody.trim()}>{noteBusy ? "Saving…" : "Save note"}</button><span className="subtle">Unsaved drafts are kept in this tab until you close it.</span></div>
          <Notice error={noteError}/>{noteMessage && <p className="subtle" role="status">{noteMessage}</p>}
        </div>
        {notes.length === 0 && <p className="subtle">No notes yet.{body ? " Select a passage to highlight it or quote it in a note." : ""}</p>}
        {notes.map(note => <article className="reader-note" key={note.id}>{note.quote && <blockquote className="reader-note-quote">{note.quote}</blockquote>}{note.body !== "Highlighted passage" && <p>{note.body}</p>}<small className="muted">{readableDate(note.createdAt)}</small></article>)}
      </section>

      <section id="panel-explore" role="tabpanel" aria-labelledby="tool-explore" hidden={tool !== "explore"} className="reader-tool-panel">
        <h2>Related items</h2>
        {item.related?.length ? <ArticleList items={item.related.slice(0, 4)} label="Related items" onChange={updated => itemQuery.setData(old => old ? { ...old, related: old.related?.map(related => related.id === updated.id ? updated : related).filter(related => !related.state.dismissed) } : old)}/>
          : <p className="subtle">No related items in your collection yet. <Link className="text-link" href="/search">Search for more.</Link></p>}
        <div className="reader-explore-tools"><button className="btn small" disabled={trailBusy} onClick={takeDeeper}>{trailBusy ? "Finding a trail…" : "Find a reading trail"}</button><Notice error={trailError}/>{lesson && <div className="reader-result"><h3>{lesson.title}</h3><p>{lesson.question}</p><p className="subtle">{lesson.provenance}</p>{lesson.items.map((related, index) => <div key={related.id} className="row-item"><span className="number">{index + 1}</span><Link className="text-link" href={`/items/${related.id}`}>{itemHeading(related)}</Link><ArrowRight size={14}/></div>)}</div>}</div>
        <details className="reader-ai-tools"><summary>AI tools</summary><Notice error={aiStatus.error} onRetry={() => void aiStatus.reload()}/>{aiStatus.loading ? <p className="subtle">Checking AI availability…</p> : aiStatus.data?.configured && aiStatus.data.enabled ? <><p className="subtle">{aiStatus.data.privacy}</p><div className="action-row"><button className="btn small" disabled={aiBusy} onClick={() => askAi("summary")}>Summarize</button><button className="btn small" disabled={aiBusy} onClick={() => askAi("challenge")}>Compare arguments</button>{aiBusy && <span className="subtle" role="status">Generating…</span>}</div></> : <p className="subtle">AI is off. <Link className="text-link" href="/settings">See setup details in Settings.</Link></p>}<Notice error={aiError}/>{aiText && <div className="reader-result"><h3>AI response</h3><p className="reader-ai-result">{aiText}</p><div className="action-row">{aiEvidence.map((evidenceId, index) => <Link className="text-link" href={`/items/${evidenceId}`} key={evidenceId}>Source {index + 1} →</Link>)}</div></div>}</details>
      </section>

      <section id="panel-details" role="tabpanel" aria-labelledby="tool-details" hidden={tool !== "details"} className="reader-tool-panel">
        <h2>Source details</h2><dl className="reader-facts"><div><dt>{item.documentId ? "Document" : "Source"}</dt><dd>{sourceLabel(item)}</dd></div><div><dt>Imported</dt><dd>{readableDate(item.ingestedAt)}</dd></div><div><dt>Available content</dt><dd>{item.textProvenance === "provider-abstract" ? "Abstract supplied by the research provider" : item.documentId ? body ? "Text extracted from your uploaded file" : "Original file; text extraction unavailable" : body ? "Text supplied by the source" : excerpt ? "Source excerpt and original link" : "Link and source details"}</dd></div></dl>
        {body && item.document?.format === "PDF" && <div className="reader-pdf-layout"><label><input type="checkbox" checked={preservePdfLines} onChange={event => {
          setPreservePdfLines(event.target.checked);
          try { sessionStorage.setItem(`rabbithole-pdf-lines:${item.documentId}`, event.target.checked ? "original" : "reflow"); } catch { /* Optional display preference. */ }
        }}/>Preserve PDF line breaks</label><p className="subtle">Useful for poetry, lists, and tables. Open the original file for page layout.</p></div>}
        <PaperProvenance item={item}/><Notice error={detailsError}/>
        <h3>Topics</h3><p className="subtle">{item.topics.map(topic => topic.name).join(" · ") || "No topics assigned."}</p>
        <button className="btn small" disabled={detailsBusy} onClick={() => { setSelectedTopics(item.topics.map(topic => topic.id)); setEditingTopics(!editingTopics); }}>Edit topics</button>
        {editingTopics && <div className="stack reader-editor"><Notice error={topics.error} onRetry={() => void topics.reload()}/>{topics.loading ? <p className="subtle">Loading topics…</p> : <div className="chip-row">{topics.data?.map(topic => <label className="pill" key={topic.id}><input type="checkbox" checked={selectedTopics.includes(topic.id)} onChange={event => setSelectedTopics(event.target.checked ? [...selectedTopics, topic.id] : selectedTopics.filter(value => value !== topic.id))}/>{topic.name}</label>)}</div>}<div className="action-row"><button className="btn small" disabled={detailsBusy || !topics.data} onClick={() => saveDetails({ topicIds: selectedTopics })}>Save topics</button><button className="reader-text-button" disabled={detailsBusy} onClick={() => setEditingTopics(false)}>Cancel</button></div></div>}
        <ConceptEditor item={item} onSaved={itemQuery.reload}/>
        {item.documentId && <div className="reader-editor"><button className="btn small" disabled={detailsBusy} onClick={() => { setTitle(item.title); setAuthor(item.author || ""); setEditing(!editing); }}>Edit title and author</button>{editing && <div className="stack reader-editor"><p className="subtle">To edit the whole book, go to <Link href="/library" className="text-link">Library</Link>.</p><label className="field">Title<input value={title} onChange={event => setTitle(event.target.value)}/></label><label className="field">Author<input value={author} onChange={event => setAuthor(event.target.value)}/></label><div className="action-row"><button className="btn small" disabled={detailsBusy || !title.trim()} onClick={() => saveDetails({ title: title.trim(), author: author.trim() || null })}>Save details</button><button className="reader-text-button" disabled={detailsBusy} onClick={() => setEditing(false)}>Cancel</button></div></div>}</div>}
      </section>
    </section>

    {(selection || highlightMessage) && <div className="reader-selection-bar" role="region" aria-label="Selected passage">
      {selection && <><button className="btn small" disabled={highlightBusy} onPointerDown={event => event.preventDefault()} onClick={captureHighlight}><Highlighter size={14}/>{highlightBusy ? "Saving…" : "Highlight selected text"}</button><button className="btn small" disabled={noteBusy || highlightBusy || selection.length > 4000} onPointerDown={event => event.preventDefault()} onClick={() => { updateDraft(noteBody, selection); window.getSelection()?.removeAllRanges(); setSelection(""); openTool("notes", true); }}>Quote in note</button></>}
      {highlightMessage && <span role="status">{highlightMessage}</span>}{highlightError && <span role="alert">{highlightError}</span>}
      <button className="icon-button" aria-label="Close selection tools" onClick={() => { window.getSelection()?.removeAllRanges(); setSelection(""); setHighlightMessage(null); setHighlightError(null); }}><X size={16}/></button>
    </div>}
  </div>;
}
