"use client";
import { useEffect, useState } from "react";
import { BookOpen, Club, Layers3, Spade } from "lucide-react";
import { DEFAULT_PROGRESS, PROGRESS_KEY, RESET_KEY, type Progress, type ProgressUpdate, applyProgress, loadProgress, readProgress, saveProgress, resetProgress as clearProgress } from "@/lib/blackjack/progress";
import { LearnMode } from "./learn-mode";
import { PracticeMode } from "./practice-mode";
import { PlayMode } from "./play-mode";
import { TableRules } from "./reference";
import { RULESET_NAME } from "@/lib/blackjack/rules";

const MODES = [
  { id: "learn", label: "Learn", detail: "Find your footing", icon: BookOpen },
  { id: "practice", label: "Practice", detail: "Sharpen your decisions", icon: Layers3 },
  { id: "play", label: "Play", detail: "Pull up a chair", icon: Spade },
] as const;
type Mode = typeof MODES[number]["id"];

export function BlackjackLab() {
  const [mode, setMode] = useState<Mode>("learn");
  const [progress, setProgress] = useState<Progress>(DEFAULT_PROGRESS);
  const [ready, setReady] = useState(false);
  const [storageMessage, setStorageMessage] = useState("");
  const [resetKey, setResetKey] = useState(0);
  useEffect(() => {
    try { setProgress(loadProgress(localStorage)); }
    catch { setStorageMessage("Browser storage is unavailable. Progress will last for this visit only."); }
    setReady(true);
    const sync = (event: StorageEvent) => {
      if (event.key !== null && !event.key.startsWith(PROGRESS_KEY)) return;
      try { setProgress(readProgress(localStorage)); }
      catch { setStorageMessage("Browser storage is unavailable. Progress will last for this visit only."); }
      if (event.key === RESET_KEY || event.key === null) setResetKey(key => key + 1);
    };
    window.addEventListener("storage", sync);
    return () => window.removeEventListener("storage", sync);
  }, []);
  function save(change: ProgressUpdate) {
    setProgress(previous => applyProgress(previous, change));
    try { setProgress(saveProgress(localStorage, change)); setStorageMessage(""); }
    catch { setStorageMessage("Browser storage is unavailable. Progress will last for this visit only."); }
  }
  function resetProgress() {
    setProgress({ ...DEFAULT_PROGRESS }); setResetKey(key => key + 1);
    try { clearProgress(localStorage); setStorageMessage("Lesson progress and coaching preference reset on this browser."); }
    catch { setStorageMessage("Progress reset for this visit. Browser storage is unavailable."); }
  }
  return <div className="bj">
    <header className="bj-hero"><div><div className="eyebrow">A RabbitHole field guide · {RULESET_NAME} rules</div><h1>Blackjack <em>Lab</em><span aria-hidden="true">♠</span></h1><p>Learn the game. Read the table.<br className="bj-mobile-break"/> Make your next move with confidence.</p></div><div className="bj-hero-note"><Club size={20}/><span>A little knowledge.<br/>A better seat at the table.</span></div></header>
    <div className="bj-mode-tabs" role="tablist" aria-label="Blackjack modes">{MODES.map(({ id, label, detail, icon: Icon }, index) => <button key={id} id={`bj-tab-${id}`} role="tab" type="button" disabled={!ready} aria-selected={mode === id} aria-controls={`bj-panel-${id}`} tabIndex={mode === id ? 0 : -1} onClick={() => setMode(id)} onKeyDown={event => {
      const offset = event.key === "ArrowRight" ? 1 : event.key === "ArrowLeft" ? -1 : 0;
      const nextIndex = event.key === "Home" ? 0 : event.key === "End" ? MODES.length - 1 : (index + offset + MODES.length) % MODES.length;
      if (offset || event.key === "Home" || event.key === "End") { event.preventDefault(); setMode(MODES[nextIndex].id); document.getElementById(`bj-tab-${MODES[nextIndex].id}`)?.focus(); }
    }}><Icon size={21}/><span><b>{label}</b><small>{detail}</small></span><span className="bj-tab-number" aria-hidden="true">0{index + 1}</span></button>)}</div>
    {!ready ? <p role="status">Opening Blackjack Lab…</p> : <>
      <section role="tabpanel" id="bj-panel-learn" aria-labelledby="bj-tab-learn" hidden={mode !== "learn"}><LearnMode key={resetKey} progress={progress} onProgress={save} onPlay={() => setMode("play")}/></section>
      <section role="tabpanel" id="bj-panel-practice" aria-labelledby="bj-tab-practice" hidden={mode !== "practice"}><PracticeMode/></section>
      <section role="tabpanel" id="bj-panel-play" aria-labelledby="bj-tab-play" hidden={mode !== "play"}><PlayMode coaching={progress.coach} onCoaching={coach => save({ type: "coach", coach })}/></section>
    </>}
    <TableRules/>
    <footer className="bj-footer"><p>At your pace. Lesson progress stays in this browser.<br/>Practice scores and virtual chips last for this visit.</p><button className="btn ghost small" onClick={resetProgress}>Reset lesson progress & preferences</button></footer>
    <p className="bj-copy" role="status">{storageMessage}</p>
  </div>;
}
