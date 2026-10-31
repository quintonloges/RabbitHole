import Link from "next/link";
import { redirect } from "next/navigation";
import { PublicApiError, publicApi } from "@/lib/public-api";
import type { CatalogParams, PublicFilters, PublicPage } from "@/lib/public-types";
import { PublicItems } from "./public-items";

export async function PublicCatalog({ params, search = false }: { params: CatalogParams; search?: boolean }) {
  const value = (key: string) => typeof params[key] === "string" ? params[key] as string : "";
  const query = new URLSearchParams();
  for (const key of ["q", "topicId", "source", "type", "from", "to"]) if (value(key)) query.set(key, value(key));
  const requested = Number(value("page") || 1); const page = Number.isSafeInteger(requested) ? Math.max(1, Math.min(100000, requested)) : 1;
  query.set("page", String(page));
  if (!search && ["q", "source", "type", "from", "to"].some(key => query.has(key))) redirect(`/search?${query}`);
  query.set("pageSize", "24");
  if (!search) query.set("sort", value("sort") === "newest" ? "newest" : "mix");
  const mixed = !search && query.get("sort") === "mix";
  const [pageResult, filterResult] = await Promise.allSettled([publicApi<PublicPage>(`/items?${query}`), publicApi<PublicFilters>("/filters")]);
  const result = pageResult.status === "fulfilled" ? pageResult.value : null;
  const filters = filterResult.status === "fulfilled" ? filterResult.value : { topics: [], sources: [], types: [] };
  const invalid = pageResult.status === "rejected" && pageResult.reason instanceof PublicApiError && pageResult.reason.status === 400;
  const error = invalid ? pageResult.reason.message : "Please try again in a moment.";
  const base = search ? "/search" : "/";
  function pageUrl(next: number) { const nextQuery = new URLSearchParams(query); nextQuery.delete("pageSize"); nextQuery.set("page", String(next)); return `${base}?${nextQuery}`; }
  function sortUrl(sort: string) { const nextQuery = new URLSearchParams(query); nextQuery.delete("pageSize"); nextQuery.delete("page"); nextQuery.set("sort", sort); return `${base}?${nextQuery}`; }
  return <div className="public-catalog">
    <div className="page-heading catalog-heading"><div>
      <h1 className="hero-title">{search ? "Search the catalog" : "Discover"}</h1>
      <p>Real articles, research, and unexpected connections. Follow your curiosity.</p>
    </div></div>
    {!search && filters.topics.length > 0 && <nav className="catalog-topics" aria-label="Browse topics">
      <Link prefetch={false} href="/" aria-current={!value("topicId") ? "page" : undefined}>All topics</Link>
      {filters.topics.map(topic => <Link prefetch={false} key={topic.id} href={`/?topicId=${encodeURIComponent(topic.id)}`} aria-current={value("topicId") === topic.id ? "page" : undefined}>{topic.name}</Link>)}
    </nav>}
    {!search && filterResult.status === "rejected" && result && <p className="subtle" role="status">Topics are temporarily unavailable. You can still browse the collection.</p>}
    {search && <form action="/search" method="get" className="public-catalog-form panel panel-pad">
      <label className="field public-query">Search
        <input name="q" defaultValue={value("q")} placeholder="Title, author, or idea" maxLength={200}/>
      </label>
      <details className="public-filter-options" open={["topicId", "source", "type", "from", "to"].some(key => Boolean(value(key)))}>
        <summary>Filter by topic, source, type or date</summary>
        <div className="public-filters">
          <label className="field">Topic<select name="topicId" defaultValue={value("topicId")}>
            <option value="">All topics</option>
            {value("topicId") && !filters.topics.some(topic => topic.id === value("topicId")) && <option value={value("topicId")}>Selected topic</option>}
            {filters.topics.map(topic => <option key={topic.id} value={topic.id}>{topic.name}</option>)}
          </select></label>
          <label className="field">Source<select name="source" defaultValue={value("source")}>
            <option value="">All sources</option>
            {value("source") && !filters.sources.includes(value("source")) && <option>{value("source")}</option>}
            {filters.sources.map(source => <option key={source} value={source}>{source}</option>)}
          </select></label>
          <label className="field">Type<select name="type" defaultValue={value("type")}>
            <option value="">All types</option>
            {value("type") && !filters.types.includes(value("type")) && <option>{value("type")}</option>}
            {filters.types.map(type => <option key={type} value={type}>{type.replaceAll("-", " ")}</option>)}
          </select></label>
          <label className="field">Published from<input type="date" name="from" defaultValue={value("from")}/></label>
          <label className="field">Published through<input type="date" name="to" defaultValue={value("to")}/></label>
        </div>
      </details>
      {filterResult.status === "rejected" && result && <p role="status">Filter choices are temporarily unavailable. You can still search.</p>}
      <div className="action-row">
        <button className="btn primary">Search</button>
        <Link prefetch={false} className="btn" href={base}>Clear</Link>
      </div>
    </form>}
    {!result ? <div className="empty" role="alert">
      <h2>{invalid ? "Check your search filters" : "The catalog is temporarily unavailable"}</h2><p>{error}</p>
      {!invalid && <a className="btn" href={pageUrl(page)}>Reload catalog</a>}
    </div> : <section aria-label="Public catalog">
      <div className="edition-heading"><h2>{value("q") ? `Results for “${value("q")}”` : !search && value("topicId") ? filters.topics.find(topic => topic.id === value("topicId"))?.name || "Selected topic" : "The collection"}</h2>
        <span>{result.total} {result.total === 1 ? "item" : "items"} · {mixed ? "Fresh finds + archive picks" : "Newest added first"}</span>
        {!search && <Link prefetch={false} className="text-link" href={sortUrl(mixed ? "newest" : "mix")}>{mixed ? "Newest added" : "Daily mix"}</Link>}
        {!search && value("topicId") && <Link prefetch={false} className="text-link" href="/">All topics →</Link>}
      </div>
      {mixed && <p className="subtle">Older essays, explainers, and research rotate daily alongside recent discoveries. Dates refer to original publication when supplied.</p>}
      {result.items.length ? <PublicItems items={result.items} start={(result.page - 1) * result.pageSize + 1}/> : <div className="empty">
        <h2>No items found</h2><p>{search ? "Try another topic, source, or search phrase." : value("topicId") ? "There are no public items in this topic yet." : "The collection is waiting for its first discoveries."}</p>
        {(search || value("topicId")) && <Link prefetch={false} className="btn" href={base}>{search ? "Clear filters" : "Browse all topics"}</Link>}
      </div>}
      <nav className="topic-pagination" aria-label="Catalog pages">
        {result.page > 1 ? <Link prefetch={false} className="btn small" href={pageUrl(result.page - 1)}>Previous</Link> : <span/>}
        <span className="subtle">Page {result.page} of {Math.max(1, Math.ceil(result.total / result.pageSize))}</span>
        {result.page * result.pageSize < result.total ? <Link prefetch={false} className="btn small" href={pageUrl(result.page + 1)}>Next</Link> : <span/>}
      </nav>
    </section>}
  </div>;
}
