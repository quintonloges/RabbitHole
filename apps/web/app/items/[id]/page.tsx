"use client";

import { useParams } from "next/navigation";
import { ItemReader } from "@/components/item-reader";

export default function ItemPage() {
  const { id } = useParams<{ id: string }>();
  return <ItemReader key={id} id={id}/>;
}
