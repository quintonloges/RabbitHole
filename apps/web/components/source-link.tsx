import Link from "next/link";
import type { Item } from "@/lib/types";
import { sourceLabel } from "@/lib/item-display";

export function SourceLink({ id, name }: { id: string; name: string }) {
  return <Link className="source-link" href={`/admin/library?sourceId=${encodeURIComponent(id)}`} title={`All items from ${name}`}>{name}</Link>;
}

export function ItemSources({ item }: { item: Item }) {
  if (item.document) return <>From {sourceLabel(item)}</>;
  // A deduplicated item can belong to several sources. Retained keys still work
  // after a subscription is removed or its feed URL changes.
  const sources = [...new Map((item.sources || []).map(source => [source.sourceKey, source])).values()];
  if (sources.length) return <>{sources.map((source, index) => <span key={source.sourceKey}>{index > 0 && ", "}<SourceLink id={source.sourceKey} name={source.name}/></span>)}</>;
  return item.sourceId ? <SourceLink id={item.sourceId} name={sourceLabel(item)}/> : <>{sourceLabel(item)}</>;
}
