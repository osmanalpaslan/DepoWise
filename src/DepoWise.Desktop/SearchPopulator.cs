using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DepoWise.Application.Ui;

namespace DepoWise.Desktop;

/// <summary>
/// Ortak seçim alanı davranışı (madde 3, kullanıcı isteği 2026-08-06): AutoCompleteBox.AsyncPopulator için
/// TEK, paylaşımlı üretici. Çekirdek mantık (25 kayıt sınırı + Türkçe-doğru arama) <see cref="SelectionSearch"/>
/// içinde SAF/test edilebilir; burada yalnız Avalonia'nın beklediği delegate imzasına sarılır.
/// <paramref name="source"/> her çağrıda YENİDEN okunur (Func) — koleksiyon sonradan yüklense/değişse bile
/// güncel içeriği kullanır (ObservableCollection referansı sabit kalsa da, "boş yükleniyor" anındaki eski
/// snapshot'a takılı kalınmaz).
/// </summary>
public static class SearchPopulator
{
    public static Func<string, CancellationToken, Task<IEnumerable<object>>> For<T>(
        Func<IEnumerable<T>> source, Func<T, string?> text)
        => (search, _) => Task.FromResult(SelectionSearch.Apply(source(), search, text).Cast<object>());
}
