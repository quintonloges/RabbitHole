export const PROGRESS_KEY = "rabbithole-blackjack-v1";
export type Progress = { completed: number[]; lesson: number; coach: boolean };
export const DEFAULT_PROGRESS: Progress = { completed: [], lesson: 0, coach: true };

export function parseProgress(value: string | null): Progress {
  try {
    const data = JSON.parse(value ?? "null");
    if (!data || typeof data !== "object") return { ...DEFAULT_PROGRESS };
    return {
      completed: Array.isArray(data.completed) ? [...new Set<number>(data.completed.filter((id: unknown) => Number.isInteger(id) && (id as number) >= 0 && (id as number) < 7))] : [],
      lesson: Number.isInteger(data.lesson) && data.lesson >= 0 && data.lesson < 7 ? data.lesson : 0,
      coach: typeof data.coach === "boolean" ? data.coach : true,
    };
  } catch { return { ...DEFAULT_PROGRESS }; }
}
