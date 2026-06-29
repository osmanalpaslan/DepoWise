namespace DepoWise.Desktop.ViewModels;

/// <summary>Köprüyle gelindiğinde ilgili kaydın detayını/işlemini otomatik açan ekranlar.</summary>
public interface IDeepLinkTarget
{
    void OpenEntity(string entityId);
}
