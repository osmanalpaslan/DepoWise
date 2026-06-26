// Bakım uyarı eşikleri + sonraki hedef — .NET AlertRules/MaintenanceService ile aynı.

export type AlertLevel = "normal" | "approaching" | "critical" | "overdue";
export type IntervalUnit = "km" | "hour" | "day";

export const Thresholds = { approaching: 0.85, critical: 0.95, overdue: 1.0 } as const;

export function alertLevel(progress: number): AlertLevel {
  if (progress >= Thresholds.overdue) return "overdue";
  if (progress >= Thresholds.critical) return "critical";
  if (progress >= Thresholds.approaching) return "approaching";
  return "normal";
}

export function progress(consumed: number, interval: number): number {
  return interval <= 0 ? 0 : consumed / interval;
}

// Sonraki hedef: km/hour → performed + interval; day → performed_date + interval gün (ms).
export function nextDue(unit: IntervalUnit, performed: number, interval: number): number {
  if (unit === "day") return performed + interval * 24 * 60 * 60 * 1000;
  return performed + interval;
}

export function consumedFor(unit: IntervalUnit, performed: number, currentOrNow: number): number {
  const c = currentOrNow - performed;
  if (unit === "day") return Math.max(0, c / (24 * 60 * 60 * 1000));
  return Math.max(0, c);
}
