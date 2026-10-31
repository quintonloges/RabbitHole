// Presentation only: preserve the source title and never infer dates from imports.
export function datedTitle(title: string, publishedAt?: string | null, now = Date.now()) {
  if (!publishedAt) return title;
  const published = new Date(publishedAt);
  if (!Number.isFinite(published.getTime()) || now - published.getTime() < 180 * 86400000) return title;
  const year = published.getUTCFullYear();
  return new RegExp(`\\(${year}\\)\\s*$|\\[${year}\\]\\s*$`).test(title) ? title : `${title} (${year})`;
}
