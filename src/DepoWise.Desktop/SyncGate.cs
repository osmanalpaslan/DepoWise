using System;
using System.Threading;
using System.Threading.Tasks;

namespace DepoWise.Desktop;

/// <summary>
/// Z1 (2026-07-22) — TEK EŞİTLEME KAPISI. Tüm eşitleme giriş noktaları buradan geçer:
/// giriş sonrası senkron, periyodik tick, manuel "Eşitle", "Yereli Sıfırla", çıkış/kapanış push'u.
///
/// SORUN (kullanıcı bulgusu): reset <c>IsSyncing</c>, periyodik tick ise <c>_businessSyncBusy</c> adında
/// AYRI bayraklar kullanıyordu → birbirlerini kilitlemiyorlardı. Reset (purge + tam çekme) sürerken tick
/// devreye girip AYNI veritabanına ikinci bir push/pull başlatabiliyor, pull imlecini çakışık yazabiliyordu.
///
/// ÇÖZÜM: tek <see cref="SemaphoreSlim"/>. Aynı anda YALNIZ bir eşitleme işi çalışır.
/// - Periyodik tick <see cref="TryEnter"/> kullanır → meşgulse ATLAR (işler birikmez).
/// - Kullanıcı tetikli işler <see cref="EnterAsync"/> ile sırasını BEKLER (zaman aşımı varsa vazgeçer).
/// </summary>
public static class SyncGate
{
    private static readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Meşgulse hemen false döner (beklemez) — periyodik tick için.</summary>
    public static bool TryEnter() => _gate.Wait(0);

    /// <summary>Sırasını bekler; zaman aşımında false (kullanıcı tetikli işler için).</summary>
    public static Task<bool> EnterAsync(int timeoutMs = 180_000) => _gate.WaitAsync(timeoutMs);

    /// <summary>Kapıyı bırakır. DAİMA finally içinde çağrılmalı.</summary>
    public static void Exit()
    {
        try { _gate.Release(); } catch (SemaphoreFullException) { /* çift release koruması */ }
    }
}
