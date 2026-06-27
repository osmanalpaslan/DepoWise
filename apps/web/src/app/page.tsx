import Image from "next/image";

export default function HomePage() {
  return (
    <main
      style={{
        fontFamily: "system-ui",
        minHeight: "100vh",
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: 16,
        background: "var(--brand-surface)",
        color: "var(--brand-on-surface)",
      }}
    >
      <Image src="/logo.png" alt="DepoWise" width={280} height={189} priority style={{ height: "auto" }} />
      <p style={{ opacity: 0.7 }}>Merkezi stok, araç ve bakım yönetimi</p>
      <code style={{ opacity: 0.5, fontSize: 12 }}>API: /api/v1/health</code>
    </main>
  );
}
