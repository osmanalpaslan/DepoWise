// Senkron mantığı — .NET SyncServer/SyncPolicy ile aynı kurallar (saf).
// Kritik işlemlerde LWW yok: operation_id + sunucu doğrulaması zorunlu.

export type SyncOpResult = "accepted" | "already_applied" | "rejected" | "conflict";

const CRITICAL = new Set([
  "stock_movement",
  "vehicle_maintenance",
  "fuel_distribution",
  "vehicle_meter",
  "material_request_approval",
]);

export const isCritical = (entityType: string): boolean => CRITICAL.has(entityType);

export interface SyncOperation {
  operationId: string;
  entityType: string;
  entityId: string;
  baseVersion?: number | null;
}

export interface PushContext {
  inboxHas: (operationId: string) => boolean;
  currentVersion: (entityType: string, entityId: string) => number | null;
  validateCritical?: (op: SyncOperation) => { ok: boolean; reason?: string };
}

// Push sonucu sınıflandırması (.NET Push ile aynı):
export function classifyPush(op: SyncOperation, ctx: PushContext): { result: SyncOpResult; reason?: string } {
  if (ctx.inboxHas(op.operationId)) return { result: "already_applied" };

  if (isCritical(op.entityType)) {
    const v = ctx.validateCritical?.(op) ?? { ok: false, reason: "Kritik işlem için sunucu doğrulayıcı gerekli." };
    return v.ok ? { result: "accepted" } : { result: "rejected", reason: v.reason };
  }

  // Düşük-riskli: base_version eşleşmezse conflict (kör LWW yok)
  const current = ctx.currentVersion(op.entityType, op.entityId);
  if (op.baseVersion != null && current != null && op.baseVersion !== current) {
    return { result: "conflict", reason: `Sürüm uyuşmazlığı: base ${op.baseVersion}, mevcut ${current}.` };
  }
  return { result: "accepted" };
}

export interface ServerChange {
  seq: number;
  valid: boolean;
  entityType: string;
  entityId: string;
}

// Pull: bozuk kayıtta sayfa rollback (throw) → cursor ilerlemez. Aksi halde nextCursor = son seq.
export function pullPage(changes: ServerChange[], afterSeq: number): { items: ServerChange[]; nextCursor: number } {
  const items: ServerChange[] = [];
  for (const c of changes) {
    if (!c.valid) {
      throw new Error("Pull sayfasında geçersiz kayıt: sayfa reddedildi, cursor ilerlemedi.");
    }
    items.push(c);
  }
  return { items, nextCursor: items.length > 0 ? items[items.length - 1]!.seq : afterSeq };
}
