// Talep durum makinesi — .NET RequestStatusMachine ile aynı. Onay STOK DEĞİŞTİRMEZ.

export type RequestStatus = "draft" | "pending" | "approved" | "rejected" | "cancelled";

const ALLOWED: Record<RequestStatus, RequestStatus[]> = {
  draft: ["pending", "cancelled"],
  pending: ["approved", "rejected", "cancelled"],
  approved: [], // terminal — stok çıkışı ayrı, açık işlem
  rejected: [],
  cancelled: [],
};

export const canTransition = (from: RequestStatus, to: RequestStatus): boolean =>
  ALLOWED[from].includes(to);

export const isTerminal = (s: RequestStatus): boolean => ALLOWED[s].length === 0;

// Belge no TLP-YYYY-NNNN (tenant/yıl). En büyük sıra + 1.
export function nextDocNo(year: number, existingDocNos: string[]): string {
  const prefix = `TLP-${year}-`;
  let max = 0;
  for (const no of existingDocNos) {
    if (no.startsWith(prefix)) {
      const n = Number(no.slice(prefix.length));
      if (Number.isInteger(n)) max = Math.max(max, n);
    }
  }
  return `${prefix}${String(max + 1).padStart(4, "0")}`;
}
