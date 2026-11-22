# Yüksek Hacimli Veri İşleme API

CSV müşteri yüklemelerini SQL Server'a arka planda aktaran .NET 8 Web API.

## Çalıştırma

1. `YuksekHacimliVeriIsleme.WebAPI/appsettings.json` içindeki `ConnectionStrings:SqlServer` değerini SQL Server ortamınıza göre ayarlayın. Dosya yükleme ayarları aynı dosyanın `CustomerImport` bölümündedir.
2. Çözüm klasöründen veritabanı tablolarını oluşturun/güncelleyin:

   ```powershell
   dotnet ef database update --project .\YuksekHacimliVeriIsleme.Infrastructure\YuksekHacimliVeriIsleme.Infrastructure.csproj --startup-project .\YuksekHacimliVeriIsleme.WebAPI\YuksekHacimliVeriIsleme.WebAPI.csproj
   ```

3. `YuksekHacimliVeriIsleme.WebAPI` projesini çalıştırın. Geliştirme ortamında Swagger arayüzü etkindir.

API controller'ı mevcut `ApiController` taban sınıfını kullanır. Controller, MediatR üzerinden CQRS command/query handler'larına yönlendirir; veritabanı okuma ve dosya işi kaydı `ICustomerImportRepository` üzerinden yapılır.

## CSV biçimi

Başlık satırı aşağıdaki alanların tümünü içermelidir (sıra ve büyük/küçük harf önemli değildir):

```text
Customer ID,First Name,Last Name,National ID,Email,Phone,Birth Date,Created Date
```

Tarih biçimleri `yyyy-MM-dd` veya `dd.MM.yyyy`; tarih-saat `yyyy-MM-dd HH:mm:ss` biçiminde olabilir. Dosya virgülle ayrılmış ve RFC 4180 tırnaklama kurallarına uygun olmalıdır. T.C. kimlik numarası checksum, e-posta, telefon, zorunlu alanlar ve uzunluklar denetlenir. Müşteri numarası veya T.C. kimlik numarası daha önce kayıtlıysa satır mükerrer sayılır.

## API

Uçlar mevcut JWT Bearer kimlik doğrulamasını kullanır. Önce `POST /api/Auth/Login` ile token alın, ardından isteklere `Authorization: Bearer <token>` başlığı ekleyin.

| Method | Endpoint | Açıklama |
|---|---|---|
| `POST` | `/api/customer-imports` | `multipart/form-data` içindeki `file` alanından CSV yükler; `202 Accepted` ve işlem bilgisi döner. |
| `GET` | `/api/customer-imports?page=1&pageSize=50` | Yükleme geçmişini sayfalı listeler. |
| `GET` | `/api/customer-imports/{id}` | İşlem durumunu, sayımları, zamanları ve ilerleme yüzdesini döner. |
| `GET` | `/api/customer-imports/{id}/errors?page=1&pageSize=100` | Satır numarası, ham satır ve hata açıklamasını sayfalı listeler. |
| `GET` | `/api/customer-imports/{id}/errors/export` | Hataları akışlı CSV olarak dışarı verir. |

İşlem durumları `Pending`, `Processing`, `Completed`, `Failed` ve `PartiallyCompleted` değerleridir. `ErrorCount` geçersiz kayıtları, `DuplicateCount` mükerrer kayıtları ayrı sayar; her iki kayıt türü de hata API'sinde raporlanır.

## Ölçek ve yapılandırma

`CustomerImport` ayarları:

- `MaxFileSizeBytes`: en fazla dosya boyutu (varsayılan 1 GiB; Kestrel ve multipart sınırları da bu değere göre ayarlanır).
- `BatchSize`: bellek kontrollü okuma/kayıt parçası (varsayılan 1.000 satır).
- `MaxConcurrentJobs`: eşzamanlı dosya işleyici sayısı (varsayılan 2).
- `UploadDirectory`: işlem sürerken dosyaların saklandığı dizin.

CSV dosyası belleğe bütünüyle alınmaz. Kayıtlar akışla okunup sınırlı parçalar halinde doğrulanır; uygun müşteri kayıtları SQL Server `SqlBulkCopy` ile, hata kayıtları ve sayaçlarla aynı transaction içinde toplu yazılır. Benzersiz veritabanı indeksleri ve transaction içindeki mükerrer denetimleri veri tutarlılığını korur. Durum ve sayımlar her parçada güncellenir. Uygulama yeniden başlatıldığında `Pending` ve `Processing` işlemler kuyruğa yeniden alınır. Tamamlanan veya başarısız olan işlemlerin geçici CSV dosyası silinir.
