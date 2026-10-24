import type { Item } from "@/lib/types";
import { ExternalLink } from "@/components/ui";

export function PaperProvenance({ item }: { item: Item }) {
  if (item.kind !== "paper") return null;
  let metadata: Record<string, unknown> = {};
  try {
    const parsed: unknown = JSON.parse(item.metadataJson || "{}");
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) metadata = parsed as Record<string, unknown>;
  } catch { /* Older or incomplete metadata still has the item's original identifiers. */ }
  const venue = typeof metadata.venue === "string" ? metadata.venue : null;
  const accessUrl = typeof metadata.openAccessUrl === "string" ? metadata.openAccessUrl : null;
  const references = Array.isArray(metadata.referencedWorks) ? metadata.referencedWorks.filter((value): value is string => typeof value === "string" && /^https:\/\/openalex\.org\/W\d+$/.test(value)) : [];
  return <div className="paper-record">
    <h3>Paper details</h3>
    <p className="subtle" style={{ lineHeight: 1.8, overflowWrap: "anywhere" }}>
      {item.provider}{item.providerId ? ` · ${item.providerId}` : ""}
      {venue && <><br/>{venue}</>}
      {item.doi && <><br/>DOI: {item.doi}</>}
      {item.arxivId && <><br/>arXiv: {item.arxivId}</>}
    </p>
    {accessUrl && <ExternalLink url={accessUrl} className="btn small">Open access copy ↗</ExternalLink>}
    {!!references.length && <details className="details"><summary>References listed by the provider</summary><p className="subtle">{references.length > 30 ? "Showing the first 30 references." : `${references.length} reference${references.length === 1 ? "" : "s"}.`} These links open the provider’s records. The papers haven’t been imported.</p><div className="chip-row">{references.slice(0, 30).map(url => <ExternalLink key={url} url={url} className="pill">{url.split("/").pop()} ↗</ExternalLink>)}</div></details>}
  </div>;
}
