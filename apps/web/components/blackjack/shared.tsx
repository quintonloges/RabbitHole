import type { Action } from "@/lib/blackjack/engine";
import { ACTION_LABELS } from "@/lib/blackjack/strategy";
import type { Feedback } from "@/lib/blackjack/coaching";

export function ActionButtons({ actions, onAction, disabled = false }: { actions: readonly Action[]; onAction: (action: Action) => void; disabled?: boolean }) {
  return <div className="bj-actions" aria-label="Available actions">{actions.map(action => <button key={action} className="btn bj-action" disabled={disabled} onClick={() => onAction(action)}>{ACTION_LABELS[action]}</button>)}</div>;
}

export function CoachFeedback({ feedback }: { feedback: Feedback | null }) {
  return <div className="bj-feedback-slot" role="status" aria-live="polite" aria-atomic="true">{feedback && <div className={`bj-feedback ${feedback.correct ? "bj-correct" : "bj-review"}`}>
    <strong>{feedback.title}</strong><p>{feedback.explanation}</p><small>{feedback.context}</small>
  </div>}</div>;
}
