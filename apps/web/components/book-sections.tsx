"use client";
import Link from "next/link";
import { useState } from "react";
import { ArrowRight } from "lucide-react";
import type { BookSection } from "@/lib/types";
import { groupBookSections, isPageSection, readingSequence, sectionPageLabel } from "@/lib/document-sections";

const pageSize = 8;

function SectionList({ sections, label, currentItemId, reader }: { sections: BookSection[]; label: string; currentItemId?: string; reader?: boolean }) {
  const [search, setSearch] = useState("");
  const initialPage = Math.max(0, Math.floor(sections.findIndex(section => section.itemId === currentItemId) / pageSize));
  const [page, setPage] = useState(initialPage);
  const filtered = sections.filter(section => `${section.title} ${sectionPageLabel(section)}`.toLowerCase().includes(search.toLowerCase()));
  const lastPage = Math.max(0, Math.ceil(filtered.length / pageSize) - 1);
  const currentPage = Math.min(page, lastPage);
  const shown = filtered.slice(currentPage * pageSize, (currentPage + 1) * pageSize);
  return <div className="section-list stack">
    {sections.length > pageSize && <label className="field">{`Find in ${label.toLowerCase()}`}<input type="search" value={search} onChange={event => { setSearch(event.target.value); setPage(0); }} placeholder={label === "Pages" ? "Page number or title" : "Section title or page"}/></label>}
    <div>{shown.map(section => <div key={section.id} className="row-item" aria-current={currentItemId === section.itemId ? "page" : undefined}>
      <div><h3>{reader ? <Link className="text-link" href={`/items/${section.itemId}`} aria-current={currentItemId === section.itemId ? "page" : undefined}>{section.title}</Link> : section.title}</h3><span className="subtle">{sectionPageLabel(section)}{currentItemId === section.itemId ? " · Reading now" : ""}</span></div>
      {!reader && <Link className="btn small" href={`/items/${section.itemId}`}>Read <ArrowRight size={13}/></Link>}
    </div>)}</div>
    {filtered.length === 0 && <p className="subtle">No sections match that search.</p>}
    {filtered.length > pageSize && <div className="action-row"><button className="btn small" disabled={currentPage === 0} onClick={() => setPage(currentPage - 1)} aria-label={`Previous ${label.toLowerCase()} results`}>Previous</button><span className="subtle" role="status">{currentPage * pageSize + 1}–{Math.min((currentPage + 1) * pageSize, filtered.length)} of {filtered.length}</span><button className="btn small" disabled={currentPage === lastPage} onClick={() => setPage(currentPage + 1)} aria-label={`Next ${label.toLowerCase()} results`}>Next</button></div>}
  </div>;
}

export function BookSections({ sections, currentItemId, reader = false, isPdf, showNavigation = true }: { sections: BookSection[]; currentItemId?: string; reader?: boolean; isPdf?: boolean; showNavigation?: boolean }) {
  const groups = groupBookSections(sections);
  const pdf = isPdf ?? sections.some(section => section.startPage != null || isPageSection(section));
  const primaryLabel = pdf ? "Chapters & ranges" : "Sections";
  const { current, sequence, index, label: sequenceLabel } = readingSequence(sections, currentItemId, pdf);
  return <nav className="book-sections stack" aria-label="Document sections">
    {reader && showNavigation && index >= 0 && <div className="action-row">{index > 0 && <Link className="btn small" href={`/items/${sequence[index - 1].itemId}`} aria-label={`Previous ${sequenceLabel}`}>← Previous</Link>}{index < sequence.length - 1 && <Link className="btn small" href={`/items/${sequence[index + 1].itemId}`} aria-label={`Next ${sequenceLabel}`}>Next →</Link>}<span className="subtle">{index + 1} of {sequence.length} {sequenceLabel === "page" ? "pages" : "sections"}</span></div>}
    {current?.isSuperseded && <p className="subtle">This is an earlier version with your saved notes and progress. Updated sections are below.</p>}
    {groups.chapters.length > 0 ? <div><h3 className="section-group-title">{primaryLabel} <span className="subtle">({groups.chapters.length})</span></h3><SectionList key={`chapters-${currentItemId || "library"}`} sections={groups.chapters} label={primaryLabel} currentItemId={currentItemId} reader={reader}/></div> : <p className="subtle">{pdf ? "No chapters were detected." : "No readable sections are available."} {groups.pages.length ? "Read individual pages below, or mark your own page ranges in the library." : `Open the original file${pdf ? ", mark a page range," : ""} or rebuild sections to try extraction again.`}</p>}
    {groups.pages.length > 0 && <details className="section-group" open={groups.chapters.length === 0 || !!groups.pages.find(section => section.itemId === currentItemId)}><summary>Pages <span className="subtle">({groups.pages.length})</span></summary><SectionList key={`pages-${currentItemId || "library"}`} sections={groups.pages} label="Pages" currentItemId={currentItemId} reader={reader}/></details>}
    {groups.previous.length > 0 && <details className="section-group" open={!!current?.isSuperseded}><summary>Previous extraction <span className="subtle">({groups.previous.length})</span></summary><p className="subtle">Earlier versions include your saved notes and progress.</p><SectionList key={`previous-${currentItemId || "library"}`} sections={groups.previous} label="Previous extraction" currentItemId={currentItemId} reader={reader}/></details>}
  </nav>;
}
