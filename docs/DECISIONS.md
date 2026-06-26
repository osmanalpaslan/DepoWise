# DECISIONS

## ADR-000 - V6 başlangıç kararları
- Web: Next.js + TypeScript strict + Drizzle + PostgreSQL.
- Masaüstü: .NET 8 + Avalonia + MVVM + Dapper + SQLite.
- Web çevrimiçi merkez; masaüstü offline-first.
- Stok hareket defteri ana kaynak; kritik operasyonlarda LWW kullanılmaz.
- Fotoğraf için file_records + storage provider; DB base64 varsayılan değildir.
- Geliştirme makinesinde dotnet host ve mutlak LocalAppData DB yolu zorunludur.

Fazlar ilerledikçe yeni kararlar tarih, bağlam, karar, alternatifler ve sonuç formatında eklenir.
