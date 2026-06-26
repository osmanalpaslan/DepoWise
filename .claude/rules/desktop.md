---
paths:
  - "src/**/*.{cs,axaml,csproj,props}"
---
# Masaüstü
- .NET 8, nullable, async/await; UI thread DB/ağ ile bloklanmaz.
- MVVM; code-behind iş kuralı içermez.
- Dapper parametreli; transaction aynı connection üzerinde taşınır.
- NumericUpDown ve aranabilir seçim ortak bileşenleri kullanılır.
- SQLite mutlak LocalAppData; Cache=Private, WAL, foreign_keys, busy_timeout.
- Debug apphost kapalı; test `.exe` değil dotnet host ile.
