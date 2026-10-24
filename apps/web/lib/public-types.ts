// Deliberately independent of private Item, Topic, Source, and personal-state types.
export type PublicTopic = { id: string; name: string };
export type PublicItem = {
  id: string; title: string; canonicalUrl: string; author?: string | null;
  publishedAt?: string | null; ingestedAt: string; type: string; provider: string;
  sourceName: string; excerpt?: string | null; topics: PublicTopic[];
  embedUrl?: string | null; imageUrl?: string | null;
};
export type PublicPage = { items: PublicItem[]; total: number; page: number; pageSize: number };
export type PublicFilters = { topics: PublicTopic[]; sources: string[]; types: string[] };
export type CatalogParams = Record<string, string | string[] | undefined>;
