import type { Source } from "@/lib/types";

type BackfillOptions = Pick<Source, "backfillEnabled" | "backfillLimit" | "backfillUrl">;

export function SourceBackfillOptions({ kind, value, onChange }: { kind: string; value: BackfillOptions; onChange: (patch: Partial<BackfillOptions>) => void }) {
  if (kind === "web-archive") return <p className="subtle">This source already discovers older links on its refresh schedule. It does not need a separate archive backfill.</p>;
  return <div style={{ marginTop: 20 }}>
    <div className="form-grid">
      <label className="field" style={{ display: "flex", alignItems: "center", gap: 9 }}>
        <input type="checkbox" checked={value.backfillEnabled} onChange={e => onChange({ backfillEnabled: e.target.checked })} style={{ width: "auto" }}/>
        Discover older content daily
      </label>
      <label className="field">Older items per day<input aria-label="Older items per day" aria-describedby="backfill-limit-help" type="number" min="1" max="10" value={value.backfillLimit} disabled={!value.backfillEnabled} onChange={e => onChange({ backfillLimit: Number(e.target.value) })}/><small id="backfill-limit-help">Up to this many new items per daily check, separate from recent refreshes.</small></label>
    </div>
    {kind === "rss" && <details className="details"><summary>Archive URL (optional)</summary><label className="field" style={{ marginTop: 12 }}>Archive sitemap or feed URL<input aria-label="Archive sitemap or feed URL" aria-describedby="backfill-url-help" type="url" value={value.backfillUrl} onChange={e => onChange({ backfillUrl: e.target.value })} placeholder="https://publisher.example/sitemap.xml"/><small id="backfill-url-help">Use an XML sitemap or archive feed on the same website. Leave blank to try the source’s available archive.</small></label></details>}
    {kind === "youtube" && <p className="subtle" style={{ marginTop: 10 }}>Older uploads use yt-dlp without an API key. Only video metadata is retrieved. Use Check YouTube to test access without importing items.</p>}
    {kind === "openalex" && <p className="subtle" style={{ marginTop: 10 }}>Older papers require an OpenAlex API key on the server. Regular refreshes still work without one.</p>}
  </div>;
}

const statusLabels: Record<string, string> = {
  pending: "Waiting for first check", ready: "More to explore", running: "Checking older content", "needs-key": "API key needed", "scan-limit": "Archive scan limit reached",
  unsupported: "Archive unavailable", unavailable: "Archive unavailable", limited: "Available archive checked", "caught-up": "Available archive checked", failed: "Needs attention", interrupted: "Check interrupted", paused: "Paused", disabled: "Daily discovery off",
};

function checkTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? "Date unknown" : date.toLocaleString(undefined, { month: "short", day: "numeric", year: "numeric", hour: "numeric", minute: "2-digit" });
}

export function SourceBackfillWarnings({ sources }: { sources: Source[] }) {
  const failures = sources.filter(source => source.kind === "youtube" && source.enabled && source.backfillEnabled && source.backfillFailureCount > 0);
  if (failures.length === 0) return null;
  return <p className="error" role="alert" style={{ fontSize: 12, lineHeight: 1.6 }}>YouTube archive needs attention for {failures.length} active {failures.length === 1 ? "source" : "sources"}. Recent video refreshes may still be healthy. Check the affected {failures.length === 1 ? "source" : "sources"} below.</p>;
}

export function SourceBackfillStatus({ source, paused }: { source: Source; paused: boolean }) {
  if (source.kind === "web-archive") return <div aria-label={`Older content for ${source.name}`} style={{ marginTop: 12 }}><p><strong>Older content</strong> · Included in this source’s regular discovery</p></div>;
  const schedule = !source.enabled ? "Source off" : paused ? "Ingestion paused" : !source.backfillEnabled ? "Daily discovery off" : statusLabels[source.backfillStatus] || source.backfillStatus || "Waiting for first check";
  const youtubeFailure = source.kind === "youtube" && source.backfillFailureCount > 0;
  return <div aria-label={`Older content for ${source.name}`} style={{ marginTop: 12 }}>
    <p><strong>Older content</strong> · {schedule}</p>
    {youtubeFailure ? <div className="error" role="alert" style={{ fontSize: 12, lineHeight: 1.6, marginTop: 8 }}><strong>YouTube archive needs attention</strong><div>{source.backfillMessage || "Older uploads could not be checked."}</div><div>Use Check YouTube to test access, or Find older now to retry importing.</div></div> : source.backfillMessage && <p>{source.backfillMessage}</p>}
    <p className="subtle">{source.backfillLastAttemptAt ? <>{source.backfillImportedCount} older items added · Last checked <time dateTime={source.backfillLastAttemptAt}>{checkTime(source.backfillLastAttemptAt)}</time></> : "No older-content check yet"}{source.enabled && source.backfillEnabled && !paused && source.backfillNextAt && <> · {youtubeFailure ? "Next retry" : "Next check"} <time dateTime={source.backfillNextAt}>{checkTime(source.backfillNextAt)}</time></>}</p>
    <p className="subtle">{source.backfillLastSuccessAt ? <>Last successful check <time dateTime={source.backfillLastSuccessAt}>{checkTime(source.backfillLastSuccessAt)}</time></> : "No successful older-content check yet"}</p>
  </div>;
}
