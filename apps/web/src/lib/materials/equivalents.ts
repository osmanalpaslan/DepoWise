// Muadil grup çözümü — .NET MaterialService.GetEquivalentGroup ile aynı (döngü güvenli BFS).
export interface EquivalentEdge {
  materialId: string;
  equivalentId: string;
}

export function equivalentGroup(materialId: string, edges: EquivalentEdge[]): Set<string> {
  const adj = new Map<string, string[]>();
  for (const e of edges) {
    (adj.get(e.materialId) ?? adj.set(e.materialId, []).get(e.materialId)!).push(e.equivalentId);
  }
  const visited = new Set<string>([materialId]);
  const queue = [materialId];
  while (queue.length > 0) {
    const cur = queue.shift()!;
    for (const n of adj.get(cur) ?? []) {
      if (!visited.has(n)) {
        visited.add(n);
        queue.push(n);
      }
    }
  }
  visited.delete(materialId);
  return visited;
}

export function assertNotSelf(materialId: string, equivalentId: string): void {
  if (materialId === equivalentId) throw new Error("Malzeme kendisine muadil olamaz.");
}
