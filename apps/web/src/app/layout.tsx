import type { Metadata } from "next";
import type { CSSProperties, ReactNode } from "react";
import "./globals.css";
import { defaultTheme, themeToCssVars } from "@/lib/theme/tokens";

export const metadata: Metadata = {
  title: "DepoWise",
  description: "DepoWise — merkezi stok, araç ve bakım yönetimi",
};

export default function RootLayout({ children }: { children: ReactNode }) {
  // Merkezi tema CSS değişkenleri kök seviyede uygulanır (firma override Faz 05'te buradan).
  const themeVars = themeToCssVars(defaultTheme) as CSSProperties;
  return (
    <html lang="tr" style={themeVars}>
      <body>{children}</body>
    </html>
  );
}
