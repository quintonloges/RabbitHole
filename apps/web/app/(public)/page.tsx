import type { Metadata } from "next";
import { PublicCatalog } from "@/components/public-catalog";
import { indexingEnabled, publicOrigin } from "@/lib/public-api";
import type { CatalogParams } from "@/lib/public-types";

export async function generateMetadata({ searchParams }: { searchParams: Promise<CatalogParams> }): Promise<Metadata> {
  const filtered = Object.keys(await searchParams).length > 0;
  return { title: "Discover · RabbitHole", description: "Explore a collection of real articles, research, and unexpected connections.", robots: { index: indexingEnabled() && !filtered, follow: true }, alternates: filtered ? undefined : { canonical: publicOrigin() } };
}
export default async function CatalogPage({ searchParams }: { searchParams: Promise<CatalogParams> }) { return <PublicCatalog params={await searchParams}/>; }
