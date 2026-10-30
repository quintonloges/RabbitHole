import { LESSONS, type LessonId } from "./lessons";

export const LEGACY_PROGRESS_KEY = "rabbithole-blackjack-v1";
export const PROGRESS_KEY = "rabbithole-blackjack-v2";
export const RESET_KEY = `${PROGRESS_KEY}:reset`;
export type Progress = { completed: LessonId[]; lesson: LessonId; coach: boolean };
export type ProgressUpdate = { type: "complete" | "lesson"; lesson: LessonId } | { type: "coach"; coach: boolean };
export const DEFAULT_PROGRESS: Progress = { completed: [], lesson: LESSONS[0].id, coach: true };
// Immutable v1 positions: do not derive these from the current lesson order.
const LEGACY_IDS: readonly LessonId[] = ["fundamentals", "actions", "hard-hands", "soft-hands", "splitting-doubling", "bankroll", "putting-it-together"];
const ids = new Set<string>(LESSONS.map(lesson => lesson.id));
const completionKey = (id: LessonId) => `${PROGRESS_KEY}:completed:${id}`;
const lessonKey = `${PROGRESS_KEY}:lesson`;
const coachKey = `${PROGRESS_KEY}:coach`;
type ProgressStorage = Pick<Storage, "getItem" | "setItem" | "removeItem">;

export function parseLegacyProgress(value: string | null): Progress {
  try {
    const data = JSON.parse(value ?? "null");
    if (!data || typeof data !== "object") return { ...DEFAULT_PROGRESS };
    const legacyId = (index: unknown) => typeof index === "number" && Number.isInteger(index) ? LEGACY_IDS[index] : undefined;
    return {
      completed: Array.isArray(data.completed) ? [...new Set<LessonId>(data.completed.map(legacyId).filter((id: LessonId | undefined): id is LessonId => !!id && ids.has(id)))] : [],
      lesson: legacyId(data.lesson) ?? DEFAULT_PROGRESS.lesson,
      coach: typeof data.coach === "boolean" ? data.coach : true,
    };
  } catch { return { ...DEFAULT_PROGRESS }; }
}

export function readProgress(storage: ProgressStorage): Progress {
  const lesson = storage.getItem(lessonKey);
  return {
    completed: LESSONS.filter(lesson => storage.getItem(completionKey(lesson.id)) === "true").map(lesson => lesson.id),
    lesson: lesson && ids.has(lesson) ? lesson as LessonId : DEFAULT_PROGRESS.lesson,
    coach: storage.getItem(coachKey) !== "false",
  };
}

export function loadProgress(storage: ProgressStorage): Progress {
  if (!storage.getItem(PROGRESS_KEY)) {
    const legacy = parseLegacyProgress(storage.getItem(LEGACY_PROGRESS_KEY));
    // Only fill missing values, including when another tab already started migration.
    for (const [key, value] of [[lessonKey, legacy.lesson], [coachKey, String(legacy.coach)], ...legacy.completed.map(id => [completionKey(id), "true"])])
      if (storage.getItem(key) === null) storage.setItem(key, value);
    storage.setItem(PROGRESS_KEY, "initialized");
    storage.removeItem(LEGACY_PROGRESS_KEY);
  }
  return readProgress(storage);
}

export function applyProgress(progress: Progress, change: ProgressUpdate): Progress {
  if (change.type === "coach") return { ...progress, coach: change.coach };
  if (change.type === "lesson") return { ...progress, lesson: change.lesson };
  return { ...progress, completed: [...new Set([...progress.completed, change.lesson])] };
}

export function saveProgress(storage: ProgressStorage, change: ProgressUpdate): Progress {
  // Each action writes just its own key. Concurrent completions and preference
  // changes cannot overwrite each other, even before storage events are delivered.
  if (change.type === "coach") storage.setItem(coachKey, String(change.coach));
  else if (change.type === "lesson") storage.setItem(lessonKey, change.lesson);
  else storage.setItem(completionKey(change.lesson), "true");
  return readProgress(storage);
}

export function resetProgress(storage: ProgressStorage): Progress {
  storage.setItem(PROGRESS_KEY, "initialized");
  storage.removeItem(LEGACY_PROGRESS_KEY);
  for (const key of [lessonKey, coachKey, ...LESSONS.map(lesson => completionKey(lesson.id))]) storage.removeItem(key);
  storage.setItem(RESET_KEY, crypto.randomUUID());
  return { ...DEFAULT_PROGRESS };
}
