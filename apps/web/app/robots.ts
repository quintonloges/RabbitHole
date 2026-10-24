import type { MetadataRoute } from "next";
import { indexingEnabled, publicOrigin } from "@/lib/public-api";
export const dynamic = "force-dynamic";
export default function robots(): MetadataRoute.Robots {
  return indexingEnabled() ? { rules: { userAgent: "*", allow: "/", disallow: ["/admin", "/api", "/search", "/?*"] }, sitemap: `${publicOrigin()}/sitemap.xml` } : { rules: { userAgent: "*", disallow: "/" } };
}
