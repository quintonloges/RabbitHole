"use client";
import { useState } from "react";

function safePlayer(value: string) {
  try {
    const url = new URL(value);
    if (url.protocol !== "https:" || url.username || url.password || url.port) return null;
    if (["www.youtube-nocookie.com", "www.youtube.com"].includes(url.hostname) && /^\/embed\/[A-Za-z0-9_-]{11}$/.test(url.pathname)) return url.href;
    if (url.hostname === "w.soundcloud.com" && url.pathname === "/player/") return url.href;
  } catch { /* Unsupported media remains an original-source link. */ }
  return null;
}
export function PublicMedia({ url, title }: { url: string; title: string }) {
  const [loaded, setLoaded] = useState(false); const src = safePlayer(url);
  if (!src) return null;
  const provider = new URL(src).hostname === "w.soundcloud.com" ? "SoundCloud" : "YouTube";
  return <div className="reader-player">{loaded ? <iframe title={title} src={src} className={provider === "SoundCloud" ? "audio-player" : "video-player"} loading="lazy" referrerPolicy="strict-origin-when-cross-origin" sandbox="allow-scripts allow-same-origin allow-presentation" allow="encrypted-media; picture-in-picture"/> : <div className="reader-player-consent"><h2>Play on {provider}</h2><p>Loading the player connects your browser to {provider}.</p><button className="btn primary" onClick={() => setLoaded(true)}>Load player →</button></div>}</div>;
}
