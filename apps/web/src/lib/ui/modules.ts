// Modül kataloğu — .NET AppModules.All ile AYNI anahtarlar (tek doğru kaynak parite).
export interface ModuleDef {
  key: string;
  label: string;
}

export const APP_MODULES: ModuleDef[] = [
  { key: "dashboard", label: "Ana Ekran" },
  { key: "companies", label: "Firmalar" },
  { key: "branches", label: "Şube / Şantiye" },
  { key: "users", label: "Kullanıcılar" },
  { key: "permissions", label: "Yetkiler" },
  { key: "definitions", label: "Tanımlar" },
  { key: "materials", label: "Malzemeler" },
  { key: "stock", label: "Stok İşlemleri" },
  { key: "vehicles", label: "Araçlar" },
  { key: "maintenance", label: "Bakım" },
  { key: "inspection", label: "Muayene / Sigorta" },
  { key: "fuel", label: "Yakıt" },
  { key: "daily_activity", label: "Günlük Faaliyet" },
  { key: "requests", label: "Malzeme Talep" },
  { key: "personnel", label: "Personel" },
  { key: "reports", label: "Raporlar" },
  { key: "files", label: "Dosya / Fotoğraf" },
  { key: "audit", label: "Sistem Logu / Audit" },
  { key: "backup", label: "Yedekleme" },
  { key: "sync", label: "Senkronizasyon" },
];
