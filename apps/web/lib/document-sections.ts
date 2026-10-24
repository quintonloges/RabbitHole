import type { BookSection } from "./types";

export function isPageSection(section: BookSection) {
  return /^page=\d+$/i.test(section.locator || "") ||
    (!section.locator && section.startPage === section.endPage && /^Page \d+$/i.test(section.title));
}

export function sectionPageLabel(section: BookSection) {
  if (!section.startPage) return "Section";
  return !section.endPage || section.endPage === section.startPage
    ? `Page ${section.startPage}`
    : `Pages ${section.startPage}–${section.endPage}`;
}

export function groupBookSections(sections: BookSection[]) {
  // The API supplies reading order, including EPUB spine/TOC order. Keep it intact.
  const active = sections.filter(section => !section.isSuperseded);
  return {
    chapters: active.filter(section => !isPageSection(section)),
    pages: active.filter(isPageSection),
    previous: sections.filter(section => section.isSuperseded),
  };
}

export function readingSequence(sections: BookSection[], currentItemId?: string, isPdf?: boolean) {
  const groups = groupBookSections(sections);
  const current = sections.find(section => section.itemId === currentItemId);
  const pdf = isPdf ?? sections.some(section => section.startPage != null || isPageSection(section));
  const sequence = current?.isSuperseded ? groups.previous.filter(section => isPageSection(section) === isPageSection(current))
    : current && isPageSection(current) ? groups.pages : groups.chapters;
  const label = current?.isSuperseded ? "previous extraction section" : current && isPageSection(current) ? "page" : pdf ? "chapter" : "section";
  return { current, sequence, index: sequence.findIndex(section => section.itemId === currentItemId), label };
}
