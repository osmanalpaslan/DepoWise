// Stok defteri çekirdek mantığı — .NET StockService ile fonksiyonel eşit (saf, in-memory).
// Bakiye yalnız hareketle değişir; negatif stok engellenir; operation_id idempotent; iptal = ters hareket.

export class NegativeStockError extends Error {}

export function applyDelta(current: number, signedQty: number, allowNegative = false): number {
  const updated = current + signedQty;
  if (!allowNegative && updated < 0) {
    throw new NegativeStockError(`Negatif stok engellendi: mevcut ${current}, talep ${-signedQty}.`);
  }
  return updated;
}

export interface Movement {
  operationId: string;
  materialId: string;
  direction: 1 | -1;
  quantity: number;
  type: "in" | "out" | "transfer" | "adjustment" | "reverse";
  reversed?: boolean;
  documentId?: string;
}

/** Bakiyeyi ve idempotency'yi yöneten saf defter (test ve parite için). */
export class Ledger {
  private balances = new Map<string, number>();
  private movements: Movement[] = [];
  private appliedOps = new Set<string>();

  balance(materialId: string): number {
    return this.balances.get(materialId) ?? 0;
  }

  private apply(mv: Movement, allowNegative = false): void {
    const updated = applyDelta(this.balance(mv.materialId), mv.direction * mv.quantity, allowNegative);
    this.balances.set(mv.materialId, updated);
    this.movements.push(mv);
    this.appliedOps.add(mv.operationId);
  }

  receiveIn(operationId: string, materialId: string, qty: number, documentId = "doc"): void {
    if (this.appliedOps.has(operationId)) return; // idempotent
    this.apply({ operationId, materialId, direction: 1, quantity: qty, type: "in", documentId });
  }

  issueOut(operationId: string, materialId: string, qty: number, documentId = "doc"): void {
    if (this.appliedOps.has(operationId)) return; // idempotent
    this.apply({ operationId, materialId, direction: -1, quantity: qty, type: "out", documentId });
  }

  /** Toplam stoğu değiştirmez; kaynak çıkış + hedef giriş (negatif guard kaynakta). */
  transfer(operationId: string, materialId: string, qty: number): void {
    if (this.appliedOps.has(operationId + ":out")) return;
    this.apply({ operationId: operationId + ":out", materialId, direction: -1, quantity: qty, type: "transfer" });
    this.apply({ operationId: operationId + ":in", materialId, direction: 1, quantity: qty, type: "transfer" });
  }

  /** İptal: belgeye ait hareketleri ters kayıtla geri alır (fiziksel silme yok). */
  reverseDocument(documentId: string): void {
    for (const mv of this.movements.filter((m) => m.documentId === documentId && !m.reversed && m.type !== "reverse")) {
      this.apply({
        operationId: mv.operationId + ":rev",
        materialId: mv.materialId,
        direction: (mv.direction * -1) as 1 | -1,
        quantity: mv.quantity,
        type: "reverse",
        documentId,
      });
      mv.reversed = true;
    }
  }

  movementCount(): number {
    return this.movements.length;
  }
}
