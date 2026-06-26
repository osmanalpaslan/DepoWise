// Merkezi tema/branding — .NET DepoWise.Application.Theming ile aynı anahtar/varsayılanlar.
// Renkler/marka metinleri bileşenlere SABİT yazılmaz; token + CSS değişkeninden gelir.

export interface ThemeTokens {
  primary: string;
  onPrimary: string;
  surface: string;
  onSurface: string;
  accent: string;
  danger: string;
  warning: string;
  success: string;
  cornerRadius: string;
}

export const defaultTheme: ThemeTokens = {
  primary: "#1F6FEB",
  onPrimary: "#FFFFFF",
  surface: "#FFFFFF",
  onSurface: "#1A1A1A",
  accent: "#0EA5E9",
  danger: "#DC2626",
  warning: "#D97706",
  success: "#16A34A",
  cornerRadius: "8",
};

export interface BrandingSettings {
  appName: string;
  companyName: string;
  logoPath?: string | null;
  contact?: string | null;
  website?: string | null;
  copyright?: string | null;
}

export const defaultBranding: BrandingSettings = {
  appName: "DepoWise",
  companyName: "DepoWise",
};

// Token'ları CSS değişkenlerine çevirir (inline style ya da :root enjeksiyonu için).
export function themeToCssVars(t: ThemeTokens): Record<string, string> {
  return {
    "--brand-primary": t.primary,
    "--brand-on-primary": t.onPrimary,
    "--brand-surface": t.surface,
    "--brand-on-surface": t.onSurface,
    "--brand-accent": t.accent,
    "--brand-danger": t.danger,
    "--brand-warning": t.warning,
    "--brand-success": t.success,
    "--brand-radius": `${t.cornerRadius}px`,
  };
}

export const SettingKeys = {
  ThemePrimary: "theme.primary",
  ThemeOnPrimary: "theme.on_primary",
  ThemeSurface: "theme.surface",
  ThemeOnSurface: "theme.on_surface",
  ThemeAccent: "theme.accent",
  ThemeDanger: "theme.danger",
  ThemeWarning: "theme.warning",
  ThemeSuccess: "theme.success",
  ThemeCornerRadius: "theme.corner_radius",
  BrandAppName: "brand.app_name",
  BrandCompanyName: "brand.company_name",
  BrandLogoPath: "brand.logo_path",
  BrandContact: "brand.contact",
  BrandWebsite: "brand.website",
  BrandCopyright: "brand.copyright",
} as const;
