import type { Metadata } from "next";
import { PublicCatalog } from "@/components/public-catalog";
import type { CatalogParams } from "@/lib/public-types";
export const metadata: Metadata = { title: "Search · RabbitHole", robots: { index: false, follow: true } };
export default async function SearchPage({ searchParams }: { searchParams: Promise<CatalogParams> }) { return <PublicCatalog params={await searchParams} search/>; }
