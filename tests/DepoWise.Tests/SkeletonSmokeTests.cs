using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Faz 01 smoke testleri: ortak sözleşmeler + yerel DB temeli çalışıyor mu.</summary>
public class SkeletonSmokeTests : IDisposable
{
    private readonly string _dbPath;

    public SkeletonSmokeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_test_" + Guid.NewGuid().ToString("N") + ".db");
    }

    [Fact]
    public void Connection_WAL_ForeignKeys_Aktif()
    {
        var factory = new SqliteConnectionFactory(_dbPath);
        using var conn = factory.Create();

        using var jc = conn.CreateCommand();
        jc.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", jc.ExecuteScalar()?.ToString(), ignoreCase: true);

        using var fc = conn.CreateCommand();
        fc.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, Convert.ToInt64(fc.ExecuteScalar()));
    }

    [Fact]
    public async Task Health_GercekYolda_WriteRead_Gecer()
    {
        var factory = new SqliteConnectionFactory(_dbPath);
        var result = await new DatabaseHealth(factory).CheckAsync();

        Assert.True(result.Ok, result.Error);
        Assert.True(result.WriteReadOk);
        Assert.True(result.ForeignKeysOn);
        Assert.Equal("wal", result.JournalMode, ignoreCase: true);
        Assert.Equal(_dbPath, result.DatabasePath);
    }

    [Fact]
    public void AppPaths_MutlakLocalAppData_Yolu()
    {
        var path = AppPaths.DatabasePath("Development");
        Assert.True(Path.IsPathRooted(path));
        Assert.Contains("Alpnex", path);
        Assert.EndsWith("alpnex.db", path);
    }

    [Fact]
    public void PageRequest_LimitNormalize()
    {
        Assert.Equal(1, new PageRequest { Limit = 0 }.NormalizedLimit());
        Assert.Equal(PageRequest.MaxLimit, new PageRequest { Limit = 9999 }.NormalizedLimit());
        Assert.Equal(50, new PageRequest { Limit = 50 }.NormalizedLimit());
    }

    [Fact]
    public void PagedResult_HasMore_CursorIleBelirlenir()
    {
        Assert.True(PagedResult<int>.Of(new[] { 1, 2 }, "c1").HasMore);
        Assert.False(PagedResult<int>.Of(new[] { 1, 2 }, null).HasMore);
    }

    [Fact]
    public void UnixTime_RoundTrip()
    {
        var now = DateTimeOffset.UtcNow;
        var ms = UnixTime.ToUnixMs(now);
        Assert.Equal(now.ToUnixTimeMilliseconds(), ms);
        Assert.Equal(ms, UnixTime.FromUnixMs(ms).ToUnixTimeMilliseconds());
    }

    [Fact]
    public void ApiError_CorrelationId_Tasinir()
    {
        var cid = Correlation.New();
        var err = ApiError.Of(ErrorCodes.Validation, "geçersiz", cid);
        Assert.Equal(ErrorCodes.Validation, err.Code);
        Assert.Equal(cid, err.CorrelationId);
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
