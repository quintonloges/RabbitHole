import { indexingEnabled, publicApi, publicOrigin } from "@/lib/public-api";
export const dynamic = "force-dynamic";
const xml = (value: string) => value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&apos;");
export async function GET() {
  const headers = { "Content-Type": "application/xml; charset=utf-8", "Cache-Control": "no-store" };
  if (!indexingEnabled()) return new Response('<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"/>', { headers });
  try {
    const records = await publicApi<{ id: string; updatedAt: string }[]>("/sitemap");
    const entries = [`<url><loc>${xml(publicOrigin())}/</loc></url>`, ...records.map(record => `<url><loc>${xml(publicOrigin())}/items/${encodeURIComponent(record.id)}</loc><lastmod>${xml(record.updatedAt)}</lastmod></url>`)];
    return new Response(`<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">${entries.join("")}</urlset>`, { headers });
  } catch { return new Response("Sitemap temporarily unavailable", { status: 503, headers: { "Cache-Control": "no-store" } }); }
}
