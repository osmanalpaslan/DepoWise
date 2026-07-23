using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database;

/// <summary>
/// PostgreSQL geçişi — Faz 2 Adım 1 (2026-07-23): sağlayıcıdan bağımsız yardımcılar.
///
/// Kod eskiden her yerde <c>SqliteConnection</c>/<c>SqliteCommand</c> (SQLite'a özel tipler) kullanıyordu.
/// Artık taban ADO.NET tipleri (<c>DbConnection</c>/<c>DbCommand</c>) kullanılıyor — hem SQLite (masaüstü)
/// hem Npgsql (sunucu-PostgreSQL) bu tipleri paylaşır.
///
/// Tek eksik: SQLite'ın kolaylık metodu <c>cmd.Parameters.AddWithValue(name, value)</c> taban
/// <c>DbParameterCollection</c>'da YOK. Bu uzantı onu taban <c>DbCommand</c> üzerinde sağlar; böylece
/// tüm çağrı yerleri yalnızca <c>.Parameters</c> kısmı silinerek (<c>cmd.AddWithValue(...)</c>) çalışır —
/// 1216 sorgu tek tek yeniden yazılmadan geçer.
/// </summary>
public static class DbCommandExtensions
{
    /// <summary>SQLite <c>AddWithValue</c>'nun sağlayıcıdan bağımsız karşılığı. null → DBNull.</summary>
    public static DbParameter AddWithValue(this DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
        return p;
    }

    /// <summary>
    /// IMMEDIATE transaction — eş zamanlı yazmaları serialize eder (negatif stok/sayaç güvenliği).
    /// Eski kod <c>SqliteConnection.BeginTransaction(deferred: false)</c> kullanıyordu; bu SQLite'a özel.
    /// SQLite'ta AYNI davranış (deferred:false) korunur; PostgreSQL'de normal transaction başlatılır
    /// (PostgreSQL eş zamanlı yazma güvenliğini satır kilidi/UPDATE ... WHERE ile sağlar — Faz 3'te ele alınır).
    /// </summary>
    public static DbTransaction BeginImmediate(this DbConnection conn)
        => conn is SqliteConnection s ? s.BeginTransaction(deferred: false) : conn.BeginTransaction();
}
