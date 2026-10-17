"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "./api";

export function useApi<T>(path: string | null, initial?: T) {
  const [data, setData] = useState<T | undefined>(initial);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(Boolean(path));
  const generation = useRef(0);
  const request = useRef<AbortController | null>(null);
  const reload = useCallback(async () => {
    request.current?.abort();
    const current = ++generation.current;
    if (!path) { setLoading(false); return; }
    const controller = new AbortController();
    request.current = controller;
    setLoading(true); setError(null);
    try {
      const next = await api<T>(path, { signal: controller.signal });
      if (current === generation.current && !controller.signal.aborted) setData(next);
    } catch (cause) {
      if (current === generation.current && !controller.signal.aborted) setError(cause instanceof Error ? cause.message : "Something went wrong.");
    } finally {
      if (current === generation.current) setLoading(false);
    }
  }, [path]);
  useEffect(() => {
    const generationRef = generation;
    const requestRef = request;
    setData(undefined);
    void reload();
    return () => { requestRef.current?.abort(); generationRef.current++; };
  }, [reload]);
  return { data, setData, error, setError, loading, reload };
}
