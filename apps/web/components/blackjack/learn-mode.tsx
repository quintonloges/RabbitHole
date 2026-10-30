"use client";
import { useRef, useState } from "react";
import { ArrowLeft, ArrowRight, Check } from "lucide-react";
import { LESSONS } from "@/lib/blackjack/lessons";
import type { Progress } from "@/lib/blackjack/progress";
import { Exercise, ActionDemo } from "./exercise";
import { Reference } from "./reference";

export function LearnMode({ progress, onProgress, onPlay }: { progress: Progress; onProgress: (progress: Progress) => void; onPlay: () => void }) {
  const [stepIndex, setStepIndex] = useState(0);
  const [attempts, setAttempts] = useState<Set<string>>(new Set());
  const heading = useRef<HTMLHeadingElement>(null);
  const lesson = LESSONS[progress.lesson];
  const safeStep = Math.min(stepIndex, lesson.steps.length - 1);
  const step = lesson.steps[safeStep];
  const lessonDone = lesson.steps.every((_, index) => attempts.has(`${progress.lesson}-${index}`));
  function chooseLesson(index: number) {
    setStepIndex(0); onProgress({ ...progress, lesson: index });
    requestAnimationFrame(() => heading.current?.focus({ preventScroll: true }));
  }
  function moveStep(index: number) {
    setStepIndex(index);
    requestAnimationFrame(() => { heading.current?.focus({ preventScroll: true }); heading.current?.scrollIntoView({ block: "nearest", behavior: "instant" }); });
  }
  function complete() {
    onProgress({ ...progress, completed: [...new Set([...progress.completed, progress.lesson])] });
  }
  return <div className="bj-learn-layout">
    <nav className="bj-lessons" aria-label="Blackjack lessons"><div className="bj-label">The field guide <span>{progress.completed.length}/7 complete</span></div>
      {LESSONS.map((entry, index) => <button key={entry.title} className={`bj-lesson-link${index === progress.lesson ? " bj-selected" : ""}`} aria-current={index === progress.lesson ? "step" : undefined} onClick={() => chooseLesson(index)}>
        <span className="bj-lesson-number">{progress.completed.includes(index) ? <Check size={16} aria-label="Completed"/> : String(index + 1).padStart(2, "0")}</span><span>{entry.title}</span>
      </button>)}<p className="bj-copy">Every lesson is open. Start anywhere, return whenever you like.</p>
    </nav>
    <article className="bj-lesson panel"><header className="bj-lesson-header"><div className="bj-label">Lesson {progress.lesson + 1} · About 3–5 minutes</div><h2>{lesson.title}</h2><p>{lesson.intro}</p><div className="bj-step-dots" aria-label={`Section ${safeStep + 1} of ${lesson.steps.length}`}>{lesson.steps.map((entry, index) => <button key={entry.title} onClick={() => moveStep(index)} aria-label={`Section ${index + 1}: ${entry.title}`} aria-current={safeStep === index ? "step" : undefined} className={safeStep === index ? "bj-current-step" : attempts.has(`${progress.lesson}-${index}`) ? "bj-attempted-step" : ""}/>)}</div></header>
      <div className="bj-lesson-body"><span className="bj-label">{safeStep + 1} / {lesson.steps.length}</span><h3 ref={heading} tabIndex={-1}>{step.title}</h3>{step.paragraphs.map(paragraph => <p key={paragraph}>{paragraph}</p>)}
        {step.demo && <ActionDemo key={`${progress.lesson}-${safeStep}-demo`} action={step.demo}/>}
        {step.chart && <Reference inline/>}
        <Exercise key={`${progress.lesson}-${safeStep}`} exercise={step.exercise} onAttempt={() => setAttempts(previous => new Set([...previous, `${progress.lesson}-${safeStep}`]))}/>
        {safeStep === lesson.steps.length - 1 && <div className="bj-completion">
          {progress.completed.includes(progress.lesson) ? <p role="status"><Check size={16}/> Lesson complete. Revisit any section whenever you like.</p> : <><button className="btn primary" disabled={!lessonDone} onClick={complete}>Complete lesson</button>{!lessonDone && <p className="bj-copy">Try each section’s exercise to mark this lesson complete. You can still move on freely.</p>}</>}
          {progress.lesson === LESSONS.length - 1 && <button className="btn" onClick={onPlay}>Take a seat at the table <ArrowRight size={15}/></button>}
        </div>}
      </div>
      <footer className="bj-lesson-footer"><button className="btn ghost" disabled={safeStep === 0 && progress.lesson === 0} onClick={() => safeStep > 0 ? moveStep(safeStep - 1) : chooseLesson(progress.lesson - 1)}><ArrowLeft size={15}/> Previous</button>
        <button className="btn" disabled={safeStep === lesson.steps.length - 1 && progress.lesson === LESSONS.length - 1} onClick={() => safeStep < lesson.steps.length - 1 ? moveStep(safeStep + 1) : chooseLesson(progress.lesson + 1)}>{safeStep === lesson.steps.length - 1 ? "Next lesson" : "Next"}<ArrowRight size={15}/></button></footer>
    </article>
  </div>;
}
