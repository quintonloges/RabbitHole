"use client";

import { useEffect, useRef, useState } from "react";
import { ChevronLeft, ChevronRight, Moon, RotateCw, ZoomIn, ZoomOut } from "lucide-react";
import { Document, Page, pdfjs } from "react-pdf";
import "react-pdf/dist/Page/TextLayer.css";
import "./pdf-viewer.css";

pdfjs.GlobalWorkerOptions.workerSrc = new URL("pdfjs-dist/build/pdf.worker.min.mjs", import.meta.url).toString();
const assets = `/pdfjs/${pdfjs.version}`;
const options = {
  cMapUrl: `${assets}/cmaps/`, standardFontDataUrl: `${assets}/standard_fonts/`, wasmUrl: `${assets}/wasm/`,
  isEvalSupported: false, withCredentials: true,
};
const appearanceKey = "rabbithole-pdf-appearance";

type Props = { documentId: string; initialPage: number; positionKey: string; title: string };

export default function PdfViewer({ documentId, initialPage, positionKey, title }: Props) {
  const viewport = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  const [pageCount, setPageCount] = useState(0);
  const [page, setPage] = useState(initialPage);
  const [pageInput, setPageInput] = useState(String(initialPage));
  const [zoom, setZoom] = useState(1);
  const [rotation, setRotation] = useState(0);
  const [dark, setDark] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [renderedPage, setRenderedPage] = useState<number | null>(null);
  const file = `/api/library/books/${documentId}/file`;
  const storageKey = `rabbithole-pdf-page:${positionKey}`;

  useEffect(() => {
    let saved: string | null = null;
    try { saved = localStorage.getItem(appearanceKey); } catch { /* Viewing works without storage. */ }
    setDark(saved ? saved === "dark" : getComputedStyle(document.documentElement).colorScheme === "dark");
    const element = viewport.current;
    if (!element) return;
    const observer = new ResizeObserver(entries => setWidth(Math.max(1, Math.floor(entries[0].contentRect.width))));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  function goToPage(value: number) {
    if (!pageCount) return;
    const next = Math.max(1, Math.min(pageCount, Number.isFinite(value) ? Math.trunc(value) : page));
    setPage(next); setPageInput(String(next));
    if (next !== page) { setRenderedPage(null); viewport.current?.scrollTo(0, 0); }
  }
  function loadDocument({ numPages }: { numPages: number }) {
    let next = initialPage;
    try {
      const saved = Number(localStorage.getItem(storageKey));
      if (Number.isInteger(saved) && saved >= 1 && saved <= numPages) next = saved;
    } catch { /* The original section remains the starting point. */ }
    next = Math.max(1, Math.min(numPages, next));
    setPageCount(numPages); setPage(next); setPageInput(String(next)); setError(null);
  }
  function rendered() {
    setRenderedPage(page);
    try { localStorage.setItem(storageKey, String(page)); } catch { /* Position is optional browser state, not read/understood progress. */ }
  }
  function failed(cause: Error) {
    setError(cause.name === "PasswordException" ? "This PDF needs a password. Open the original file in your PDF app."
      : "This PDF could not be displayed. Try again, use Extracted text, or open the original file.");
  }
  function retry() { setError(null); setPageCount(0); setRenderedPage(null); setAttempt(value => value + 1); }

  return <section className="pdf-viewer" aria-label={`PDF viewer: ${title}`}>
    <div className="pdf-toolbar" aria-label="PDF controls">
      <div className="pdf-page-controls">
        <button className="icon-button" aria-label="Previous PDF page" disabled={!pageCount || page <= 1 || !!error} onClick={() => goToPage(page - 1)}><ChevronLeft size={18}/></button>
        <form onSubmit={event => { event.preventDefault(); goToPage(Number(pageInput)); }}>
          <label>Page <input aria-label="PDF page number" type="number" min="1" max={pageCount || 1} value={pageInput} disabled={!pageCount || !!error} onChange={event => setPageInput(event.target.value)} onBlur={() => goToPage(Number(pageInput))}/></label>
          <span>of {pageCount || "…"}</span>
        </form>
        <button className="icon-button" aria-label="Next PDF page" disabled={!pageCount || page >= pageCount || !!error} onClick={() => goToPage(page + 1)}><ChevronRight size={18}/></button>
      </div>
      <div className="pdf-zoom-controls">
        <button className="icon-button" aria-label="Zoom out" disabled={zoom <= 0.5 || !!error} onClick={() => setZoom(value => Math.max(0.5, value - 0.25))}><ZoomOut size={17}/></button>
        <button className="reader-text-button pdf-fit" title="Fit page width" onClick={() => setZoom(1)}>{zoom === 1 ? "Fit width" : `${Math.round(zoom * 100)}%`}</button>
        <button className="icon-button" aria-label="Zoom in" disabled={zoom >= 3 || !!error} onClick={() => setZoom(value => Math.min(3, value + 0.25))}><ZoomIn size={17}/></button>
        <button className="icon-button" aria-label="Rotate PDF page" disabled={!!error} onClick={() => setRotation(value => (value + 90) % 360)}><RotateCw size={17}/></button>
      </div>
      <button className="btn small pdf-dark-toggle" aria-pressed={dark} title="Dark pages also change image colors" onClick={() => {
        const next = !dark; setDark(next);
        try { localStorage.setItem(appearanceKey, next ? "dark" : "original"); } catch { /* Optional display preference. */ }
      }}><Moon size={15}/>Dark pages</button>
    </div>
    <div ref={viewport} className="pdf-viewport" tabIndex={0} role="region" aria-label="PDF pages" data-dark={dark}>
      {error ? <div className="pdf-message"><p role="alert">{error}</p><div className="action-row"><button className="btn small" onClick={retry}>Retry PDF</button><a className="btn small" href={`${file}#page=${page}`} target="_blank" rel="noopener noreferrer">Open original file</a></div></div>
        : <Document key={attempt} file={file} options={options} suspense={false} onLoadSuccess={loadDocument} onLoadError={failed} onSourceError={failed}
          onPassword={() => setError("This PDF needs a password. Open the original file in your PDF app.")}
          loading={<p className="pdf-message" role="status">Loading original PDF…</p>} error={<p className="pdf-message" role="alert">PDF unavailable.</p>}>
          {!!pageCount && width > 0 && <Page key={page} pageNumber={page} width={Math.max(1, width - 24)} scale={zoom} rotate={rotation}
            devicePixelRatio={Math.min(window.devicePixelRatio || 1, 2)} renderAnnotationLayer={false}
            onRenderSuccess={rendered} onLoadError={failed} onRenderError={failed}
            loading={<p className="pdf-message" role="status">Rendering page {page}…</p>}/>}
        </Document>}
    </div>
    <div className="pdf-caption"><span role="status">{renderedPage && !error ? `Page ${renderedPage} of ${pageCount}` : "Original PDF"}</span><span>{dark ? "Dark pages change image colors." : "Original page colors."}</span></div>
  </section>;
}
