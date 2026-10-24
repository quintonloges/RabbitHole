"use client";

import Link from "next/link";
import { useId, useRef } from "react";
import { List, X } from "lucide-react";
import type { BookSection } from "@/lib/types";
import { readingSequence } from "@/lib/document-sections";
import { BookSections } from "./book-sections";
import { Notice } from "./ui";

type Props = { sections?: BookSection[]; currentItemId: string; isPdf?: boolean; loading: boolean; error: string | null; onRetry: () => void; footer?: boolean };

export function ReaderNavigation({ sections, currentItemId, isPdf, loading, error, onRetry, footer = false }: Props) {
  const dialog = useRef<HTMLDialogElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const heading = useId();
  const { sequence, index, label, current } = readingSequence(sections || [], currentItemId, isPdf);
  const previous = index > 0 ? sequence[index - 1] : null;
  const next = index >= 0 && index < sequence.length - 1 ? sequence[index + 1] : null;
  if (footer && !previous && !next) return null;
  return <>
    <nav className="reader-document-nav" aria-label={footer ? "Continue reading" : "Document navigation"}>
      {previous ? <Link className="text-link" href={`/items/${previous.itemId}`} aria-label={`Previous ${label}`}>← Previous</Link> : <span/>}
      <div className="reader-location">
        {!footer && <button ref={trigger} className="reader-text-button" onClick={() => dialog.current?.showModal()}><List size={14}/>Contents</button>}
        {index >= 0 && <span>{index + 1} of {sequence.length} {label === "page" ? "pages" : "sections"}</span>}
        {!footer && loading && !sections && <span role="status">Loading navigation…</span>}
        {!footer && error && <button className="reader-text-button" onClick={onRetry}>Retry navigation</button>}
      </div>
      {next ? <Link className="text-link" href={`/items/${next.itemId}`} aria-label={`Next ${label}`}>Next →</Link> : <span/>}
    </nav>
    {!footer && <>
      {current?.isSuperseded && <p className="subtle">This is an earlier version with your saved notes and progress.</p>}
      <dialog ref={dialog} className="reader-contents-dialog" aria-labelledby={heading} onClose={() => trigger.current?.focus()} onKeyDownCapture={event => {
        if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); dialog.current?.close(); }
      }} onClick={event => {
        if (event.target !== event.currentTarget) return;
        const bounds = event.currentTarget.getBoundingClientRect();
        if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.current?.close();
      }}>
        <div className="reader-dialog-heading"><h2 id={heading}>Table of contents</h2><button className="icon-button" aria-label="Close contents" onClick={() => dialog.current?.close()} autoFocus><X size={18}/></button></div>
        <Notice error={error} onRetry={onRetry}/>
        {loading && !sections ? <p role="status" className="subtle">Loading sections…</p>
          : sections && <BookSections sections={sections} currentItemId={currentItemId} isPdf={isPdf} reader showNavigation={false}/>}
      </dialog>
    </>}
  </>;
}
