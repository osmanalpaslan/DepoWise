using System.Data;
using System.Data.Common;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Tests;

/// <summary>
/// ═══ N+1 GUARD ALTYAPISI (ARA İŞ 5 / ALT FAZ 3, §14) ═══
///
/// Bir servis çağrısının kaç SQL komutu ürettiğini sayar. Bağlantıyı saran ince bir kabuktur:
/// <see cref="DbConnection.CreateDbCommand"/> çağrıldığında sayacı artırır ve <b>gerçek</b> komutu
/// döner — böylece davranış hiç değişmez, yalnız ölçülür.
///
/// <b>Neden gerekli:</b> "Onaylamalarım" listesi önce satır başına ek sorgu çalıştırıyordu (sıra
/// kontrolü). Tek sorguya çevrildi; bu sayaç, ileride biri döngü içinde sorgu eklerse testin
/// kırılmasını sağlar. Sonucun doğruluğu ayrıca ilgili testte kontrol edilir — sayaç tek başına
/// yeterli bir kanıt değildir.
/// </summary>
public sealed class SayanFabrika : IDbConnectionFactory
{
    private readonly IDbConnectionFactory _ic;
    private int _komut;

    public SayanFabrika(IDbConnectionFactory ic) => _ic = ic;

    /// <summary>Bu fabrika üzerinden açılan bağlantılarda oluşturulan toplam komut sayısı.</summary>
    public int KomutSayisi => Volatile.Read(ref _komut);

    public void Sifirla() => Interlocked.Exchange(ref _komut, 0);

    public string? DatabasePath => _ic.DatabasePath;

    public DbConnection Create() => new SayanBaglanti(_ic.Create(), () => Interlocked.Increment(ref _komut));

    private sealed class SayanBaglanti : DbConnection
    {
        private readonly DbConnection _ic;
        private readonly Action _say;

        public SayanBaglanti(DbConnection ic, Action say) { _ic = ic; _say = say; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString
        {
            get => _ic.ConnectionString;
            set => _ic.ConnectionString = value!;
        }

        public override string Database => _ic.Database;
        public override string DataSource => _ic.DataSource;
        public override string ServerVersion => _ic.ServerVersion;
        public override ConnectionState State => _ic.State;

        public override void ChangeDatabase(string databaseName) => _ic.ChangeDatabase(databaseName);
        public override void Close() => _ic.Close();
        public override void Open() => _ic.Open();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => _ic.BeginTransaction(isolationLevel);

        /// <summary>Sayaç BURADA artar; dönen komut gerçek bağlantının komutudur (davranış değişmez).</summary>
        protected override DbCommand CreateDbCommand()
        {
            _say();
            return _ic.CreateCommand();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _ic.Dispose();
            base.Dispose(disposing);
        }
    }
}
