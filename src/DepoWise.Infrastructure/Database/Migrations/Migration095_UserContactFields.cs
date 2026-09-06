using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ KULLANICI İLETİŞİM ALANLARI (kullanıcı isteği 2026-09-06) ═══
///
/// <b>Kullanıcının isteği:</b> <i>"kullanıcı yeni kayıt formunu analiz et ve olması gereken eksik
/// alanları ekle. benim gözlemlediğim iki eksik alan cep telefonu ve mail, fazlası varsa onları da ekle."</i>
///
/// <b>Neden şema değişikliği ZORUNLU.</b> <c>users</c> tablosunda bugüne kadar yalnız
/// <c>username · password_hash · full_name · is_active</c> vardı; <b>ne e-posta ne telefon sütunu
/// yoktu.</b> Kullanıcıya nasıl ulaşılacağı hiçbir yerde tutulmuyordu, dolayısıyla istek mevcut
/// şemayla karşılanamazdı. (<c>personnel</c> tablosunda <c>phone</c> var ama her kullanıcı bir
/// personel kaydına bağlı değildir — ikisi ayrı kavramdır.)
///
/// <b>Eklenen sütunlar ve gerekçeleri:</b>
/// <list type="bullet">
///   <item><c>email</c> — kullanıcının saydığı ilk eksik. Ayrıca ileride bakım/muayene uyarılarının
///         e-posta ile gönderilebilmesinin ön koşuludur (bugün gönderim YOK).</item>
///   <item><c>phone</c> — kullanıcının saydığı ikinci eksik (cep telefonu).</item>
///   <item><c>title</c> — unvan / görev. "Kim bu kullanıcı" sorusunun kullanıcı adından
///         anlaşılmadığı durumlar için; personel kaydı olmayan hesaplarda tek ipucu budur.</item>
///   <item><c>notes</c> — serbest not (ör. "şantiye tableti", "muhasebe ortak hesabı").</item>
/// </list>
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız <c>ADD COLUMN … NULL</c>. Hiç <c>UPDATE</c>, <c>DELETE</c>,
/// backfill, varsayılan değer ya da <c>NOT NULL</c> kısıtı YOKTUR → mevcut kullanıcı kayıtları
/// hiçbir şekilde değişmez, yalnız yeni sütunlar boş olarak eklenir. Girişi, yetkileri ve şifreleri
/// etkileyen tek bir satır bile yoktur. Geri alma: dört <c>DROP COLUMN</c> + <c>schema_migrations</c>
/// satırının silinmesi.
///
/// <b>SENKRON:</b> <c>users</c> zaten senkronlanan iş tablolarındandır; yeni sütunlar da alan
/// listesinden okunur, ayrıca bir iş gerekmez. Eski sürüm bir masaüstü bu sütunları görmezden gelir
/// (ileri/geri uyumluluk korunur).
///
/// <b>Gizlilik:</b> e-posta ve telefon kişisel veridir; <c>AuditFields</c> etiketleri eklenmiştir ki
/// denetim kaydında Türkçe ve anlaşılır görünsün. Parola özeti gibi gizli sütunlarla aynı kefeye
/// KONULMAZ — bunlar ekranda gösterilen alanlardır.
/// </summary>
public sealed class Migration095_UserContactFields : IMigration
{
    public int Version => 95;
    public string Name => "user_contact_fields";

    private static readonly (string Column, string Type)[] Sutunlar =
    {
        ("email", "TEXT"),   // e-posta adresi
        ("phone", "TEXT"),   // cep telefonu
        ("title", "TEXT"),   // unvan / görev
        ("notes", "TEXT"),   // serbest not
    };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var (sutun, tip) in Sutunlar)
        {
            if (DbIntrospect.ColumnExists(conn, tx, "users", sutun)) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"ALTER TABLE users ADD COLUMN {sutun} {tip} NULL;";
            cmd.ExecuteNonQuery();
        }
    }
}
