using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DepoWise.Application.Ui;

/// <summary>
/// "Sabit tanım" (lookup) açılır listesinin SAF çekirdeği (kullanıcı isteği 2026-08-08): Türkçe-doğru "içerir"
/// araması + 25'lik SAYFALAMA. Masaüstü LookupBox kontrolü bunu kullanır (UI'dan bağımsız → test edilebilir).
/// Arama boşsa tüm kayıtlar; her sayfa <paramref name="pageSize"/> kadar (varsayılan 25). Sayfa taşarsa son
/// geçerli sayfaya çekilir (kullanıcı filtreleyince eldeki sayfa numarası aşınca boş liste görünmez).
/// </summary>
public static class LookupPaging
{
    private static readonly CompareInfo Tr = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    public sealed record Result<T>(IReadOnlyList<T> Items, int Page, int TotalPages, int TotalCount);

    public static Result<T> Apply<T>(IReadOnlyList<T> all, Func<T, string?> display, string? search, int page, int pageSize = 25)
    {
        if (pageSize < 1) pageSize = 25;
        all ??= Array.Empty<T>();

        var s = search?.Trim();
        List<T> filtered = string.IsNullOrEmpty(s)
            ? all.ToList()
            : all.Where(x => Tr.IndexOf(display(x) ?? "", s, CompareOptions.IgnoreCase) >= 0).ToList();

        int total = filtered.Count;
        int totalPages = total == 0 ? 1 : (total + pageSize - 1) / pageSize;
        if (page < 1) page = 1;
        if (page > totalPages) page = totalPages;

        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new Result<T>(items, page, totalPages, total);
    }
}
