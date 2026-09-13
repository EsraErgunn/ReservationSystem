# Etkinlik Bilet Rezervasyon Sistemi — Gereksinim Dokümanı

**Versiyon:** 0.1 (taslak)
**Tarih:** 11 Eylül 2026
**Durum:** Faz 1 öncesi kapsam netleştirme

---

## 1. Projenin amacı

Kullanıcıların bir etkinlik için koltuk seçip, seçtikleri koltuğu geçici olarak rezerve ederek ödeme yapabildiği, eşzamanlı kullanıcı yükü altında tutarlı çalışan bir bilet satış sistemi geliştirmek.

Sistemin ayırt edici özelliği basit bir CRUD uygulaması olmaması; **race condition, concurrency kontrolü, geçici rezervasyon (hold) yönetimi ve dış ödeme servisi entegrasyonu** gibi gerçek üretim problemlerini çözmesi.

### 1.1 Neden etkinlik bileti (randevu sistemi yerine)

Her iki senaryo da aynı mimariyi kullanır, ancak etkinlik bileti seçildi çünkü:

- Aynı anda çok sayıda kullanıcının **aynı kaynağa** (koltuk) talip olması doğal bir senaryo — concurrency problemini yapay kurgulamaya gerek kalmıyor
- "Bilet satışı açıldı" anındaki ani yük patlaması, load testing ve rate limiting konularını gerçekçi kılıyor
- Koltuk haritası, gerçek zamanlı doluluk güncellemesi gibi frontend tarafında da öğretici bir yüzey sunuyor

Aynı domain modeli, `Event` → `Kurum/Hizmet`, `Seat` → `Zaman Slotu` dönüşümüyle randevu sistemine uyarlanabilir.

---

## 2. Kapsam

### 2.1 Kapsam içinde (Faz 1–4)

| Alan | Açıklama |
|---|---|
| Kullanıcı yönetimi | Kayıt, giriş, JWT tabanlı kimlik doğrulama |
| Etkinlik yönetimi | Etkinlik ve salon/koltuk tanımlama (admin) |
| Koltuk seçimi | Koltuk haritası görüntüleme, müsaitlik sorgulama |
| Geçici rezervasyon (hold) | Seçilen koltuğun süreli olarak kilitlenmesi |
| Ödeme | iyzico Checkout Form entegrasyonu, sonuç doğrulama |
| Rezervasyon yaşam döngüsü | Hold → Confirmed / Expired / Cancelled geçişleri |
| Gerçek zamanlı güncelleme | Koltuk doluluk durumunun anlık yayınlanması (SignalR) |
| Dayanıklılık | Redis cache, rate limiting, yük testi |
| Üretim hazırlığı | Docker Compose, CI/CD, yapılandırılmış loglama |

### 2.2 Kapsam dışında (bu sürümde yapılmayacak)

- Koltuksuz (genel giriş) bilet tipleri
- Taksitli ödeme ve kampanya/indirim kodu yönetimi
- Bilet devri / ikinci el satış
- Mobil uygulama
- Çok dilli arayüz
- E-fatura / muhasebe entegrasyonu

> Bu maddeler mimariyi bozmadan sonradan eklenebilecek şekilde tasarlanacak, ancak ilk sürümde uygulanmayacak.

---

## 3. Aktörler

| Aktör | Tanım |
|---|---|
| **Ziyaretçi** | Giriş yapmamış kullanıcı. Etkinlikleri ve müsaitliği görüntüleyebilir, rezervasyon yapamaz. |
| **Kullanıcı** | Kayıtlı kullanıcı. Koltuk seçip rezervasyon oluşturabilir, ödeme yapabilir, kendi biletlerini görüntüleyebilir. |
| **Admin** | Etkinlik ve salon tanımlayan, rezervasyonları görüntüleyen yetkili kullanıcı. |
| **Ödeme sağlayıcısı** | iyzico. Sistem dışı aktör; ödeme akışını yürütür ve sonucu callback ile bildirir. |

---

## 4. Domain varlıkları (kavramsal)

> Not: Bu bölüm veritabanı tablosu tasarımı değildir. Tablo yapısı, indeksler ve ilişkiler bir sonraki adımda bu kavramlardan türetilecektir.

### 4.1 User (Kullanıcı)
Sisteme kayıtlı kişi. Rezervasyonların sahibi.

### 4.2 Venue (Mekân)
Etkinliğin gerçekleştiği salon. Koltuk düzenini tanımlar.

### 4.3 Seat (Koltuk)
Bir mekâna ait fiziksel koltuk. Sıra ve numara bilgisi taşır. Mekâna göre sabittir — etkinlikten bağımsız tanımlanır.

### 4.4 Event (Etkinlik)
Belirli bir mekânda, belirli bir tarihte gerçekleşen etkinlik. Bilet satışının açılış/kapanış zamanını taşır.

### 4.5 EventSeat (Etkinlik Koltuğu)
Bir etkinlikteki belirli bir koltuğun satılabilir hâli. Fiyat bilgisi ve **müsaitlik durumu** bu varlıkta tutulur.

> **Kritik tasarım kararı:** Müsaitlik `Seat` üzerinde değil, `EventSeat` üzerinde tutulur. Aynı koltuk farklı etkinliklerde bağımsız olarak satılabilmelidir. Concurrency kontrolü (version/rowversion kolonu) da bu varlık üzerinde uygulanacaktır.

### 4.6 Reservation (Rezervasyon)
Bir kullanıcının bir veya birden fazla `EventSeat` için oluşturduğu rezervasyon kaydı. Yaşam döngüsü durumunu taşır.

### 4.7 Payment (Ödeme)
Bir rezervasyona ait ödeme girişimi. iyzico tarafındaki işlem kimliğini ve sonucunu saklar. Bir rezervasyonun birden fazla ödeme denemesi olabilir (başarısız deneme sonrası tekrar).

---

## 5. Rezervasyon yaşam döngüsü

```
[Oluşturuldu] ──hold başarılı──> [Held] ──ödeme onaylandı──> [Confirmed]
                                    │
                                    ├──süre doldu──────────> [Expired]
                                    │
                                    ├──ödeme başarısız─────> [Failed]
                                    │
                                    └──kullanıcı iptal─────> [Cancelled]
```

### Durum tanımları

| Durum | Anlamı | Koltuk durumu |
|---|---|---|
| `Held` | Koltuk geçici olarak kilitlendi, ödeme bekleniyor | Başkasına satılamaz |
| `Confirmed` | Ödeme başarıyla doğrulandı, bilet kesinleşti | Kalıcı olarak dolu |
| `Expired` | Hold süresi ödeme tamamlanmadan doldu | Serbest bırakıldı |
| `Failed` | Ödeme reddedildi veya hata aldı | Serbest bırakıldı |
| `Cancelled` | Kullanıcı ödeme öncesi vazgeçti | Serbest bırakıldı |

---

## 6. İş kuralları

### 6.1 Hold (geçici rezervasyon) kuralları

- **BR-01:** Kullanıcı koltuk seçtiğinde, koltuk **10 dakika** süreyle hold durumuna alınır.
- **BR-02:** Hold süresi dolduğunda koltuk otomatik olarak serbest bırakılır. Bu işlem kullanıcı hiçbir aksiyon almasa bile gerçekleşmelidir (arka plan görevi).
- **BR-03:** Bir kullanıcı aynı anda en fazla **6 koltuk** hold edebilir.
- **BR-04:** Aynı koltuk için aynı anda yalnızca bir aktif hold olabilir.
- **BR-05:** Hold süresi ödeme ekranına geçildikten sonra uzatılmaz — kullanıcı süre dolmadan ödemeyi tamamlamalıdır.

### 6.2 Concurrency kuralları

- **BR-06:** İki kullanıcı aynı koltuğu aynı anda seçtiğinde, işlemi ilk tamamlayan kazanır; diğeri açık ve anlaşılır bir hata mesajı alır (sessiz başarısızlık olmamalıdır).
- **BR-07:** Koltuk müsaitlik kontrolü ile hold yazma işlemi atomik olmalıdır — kontrol ile yazma arasında başka bir işlemin araya girmesi engellenmelidir.
- **BR-08:** Aynı isteğin ağ hatası veya çift tıklama nedeniyle tekrarlanması durumunda, ikinci istek yeni bir rezervasyon oluşturmamalıdır (idempotency).

### 6.3 Ödeme kuralları

- **BR-09:** Ödeme tutarı **her zaman sunucu tarafında** hesaplanır. İstemciden gelen fiyat bilgisine güvenilmez.
- **BR-10:** Rezervasyonu `Confirmed` duruma geçiren tek şey, ödeme sağlayıcısına yapılan **sunucu taraflı doğrulama sorgusudur**. İstemcinin "ödeme başarılı" bildirimi tek başına yeterli değildir.
- **BR-11:** Ödeme başarısız olduğunda hold serbest bırakılır ve kullanıcıya tekrar deneme imkânı sunulur.
- **BR-12:** Aynı ödeme sonucu birden fazla kez işlenmemelidir (idempotency — işlenmiş işlem kimlikleri kaydedilir).
- **BR-13:** Bir kullanıcı yalnızca kendisine ait rezervasyon için ödeme başlatabilir.

### 6.4 Erişim kuralları

- **BR-14:** Rezervasyon oluşturma ve ödeme başlatma işlemleri kimlik doğrulaması gerektirir.
- **BR-15:** Kullanıcı yalnızca kendi rezervasyonlarını görüntüleyebilir; admin tümünü görüntüleyebilir.
- **BR-16:** Etkinlik ve mekân tanımlama işlemleri yalnızca admin rolüne açıktır.
- **BR-17:** Bilet satış başlangıç tarihinden önce veya bitiş tarihinden sonra rezervasyon oluşturulamaz.

---

## 7. Fonksiyonel gereksinimler

| ID | Gereksinim | Öncelik |
|---|---|---|
| FR-01 | Kullanıcı kayıt olabilmeli ve giriş yapabilmeli | Yüksek |
| FR-02 | Kullanıcı etkinlikleri listeleyebilmeli ve detayını görebilmeli | Yüksek |
| FR-03 | Kullanıcı bir etkinliğin koltuk haritasını ve güncel doluluğunu görebilmeli | Yüksek |
| FR-04 | Kullanıcı bir veya daha fazla koltuk seçip hold oluşturabilmeli | Yüksek |
| FR-05 | Sistem hold süresini kullanıcıya geri sayım olarak göstermeli | Orta |
| FR-06 | Kullanıcı ödeme akışını başlatabilmeli ve sonucu görebilmeli | Yüksek |
| FR-07 | Sistem ödeme sonucunu sunucu tarafında doğrulayıp rezervasyonu kesinleştirmeli | Yüksek |
| FR-08 | Süresi dolan hold'lar otomatik olarak temizlenmeli | Yüksek |
| FR-09 | Koltuk doluluk değişiklikleri diğer kullanıcılara anlık yansımalı | Orta |
| FR-10 | Kullanıcı kendi rezervasyon/bilet geçmişini görüntüleyebilmeli | Orta |
| FR-11 | Kullanıcı ödeme öncesi rezervasyonunu iptal edebilmeli | Orta |
| FR-12 | Admin etkinlik, mekân ve koltuk düzeni tanımlayabilmeli | Orta |
| FR-13 | Admin bir etkinliğin satış durumunu görüntüleyebilmeli | Düşük |

---

## 8. Fonksiyonel olmayan gereksinimler

### 8.1 Performans ve ölçeklenebilirlik

- **NFR-01:** Koltuk müsaitlik sorgusu, 500 eşzamanlı kullanıcı altında p95 < 300 ms yanıt vermeli.
- **NFR-02:** Sık okunan ve nadiren değişen veriler (etkinlik listesi, koltuk düzeni) cache'lenmeli.
- **NFR-03:** Sistem, yük testi ile ölçülebilir bir kapasite sınırına sahip olmalı; sınır aşıldığında çökmek yerine kontrollü şekilde reddetmeli.

### 8.2 Güvenlik

- **NFR-04:** Kart bilgisi hiçbir aşamada uygulama sunucusuna ulaşmamalı veya loglanmamalı.
- **NFR-05:** API anahtarları ve gizli bilgiler kaynak kodda veya versiyon kontrolünde bulunmamalı.
- **NFR-06:** Ödeme ve rezervasyon endpoint'leri rate limiting ile korunmalı.
- **NFR-07:** Tüm dış iletişim HTTPS üzerinden yapılmalı.
- **NFR-08:** Yetkilendirme kontrolleri istemci tarafına bırakılmamalı; her istekte sunucuda doğrulanmalı.

### 8.3 Gözlemlenebilirlik

- **NFR-09:** Yapılandırılmış loglama uygulanmalı; her isteğe izlenebilir bir korelasyon kimliği eşlik etmeli.
- **NFR-10:** Rezervasyon ve ödeme akışındaki her durum geçişi loglanmalı.
- **NFR-11:** Sistem sağlık durumu bir health check endpoint'i üzerinden sorgulanabilmeli.

### 8.4 Sürdürülebilirlik

- **NFR-12:** İş mantığı, veritabanı ve dış servislerden bağımsız olarak test edilebilmeli.
- **NFR-13:** Ödeme sağlayıcısı, iş mantığına dokunulmadan değiştirilebilmeli (arayüz üzerinden soyutlama).
- **NFR-14:** Tüm bağımlılıklar (veritabanı, cache, uygulama) tek komutla ayağa kaldırılabilmeli.

---

## 9. Teknoloji seçimleri

| Katman | Seçim | Gerekçe |
|---|---|---|
| Frontend | React + TypeScript (Vite) | Mevcut uzmanlık alanı; TanStack Query ile sunucu durumu yönetimi |
| Backend | ASP.NET Core | Hedeflenen full stack yönü; Clean Architecture ile iyi uyum |
| Veritabanı | PostgreSQL | Açık kaynak, güçlü concurrency desteği, Docker ile kolay kurulum |
| Cache / hold | Redis | TTL desteği hold mekanizması için doğal çözüm |
| Gerçek zamanlı | SignalR | .NET ekosistemiyle yerleşik entegrasyon |
| Ödeme | iyzico | Türkiye pazarına uygun; TRY, 3D Secure yerleşik |
| Mimari | Clean Architecture | Bağımlılığın içe akması, test edilebilirlik, sağlayıcı değiştirilebilirliği |

---

## 10. Varsayımlar ve açık sorular

### Varsayımlar
- Ödeme entegrasyonu geliştirme boyunca iyzico sandbox ortamında test edilecek; gerçek para akışı olmayacak.
- Tek para birimi kullanılacak (TRY).
- Bilet PDF/QR üretimi ilk sürümde basit tutulacak.

### Açık sorular (ilerledikçe netleşecek)
- Concurrency stratejisi olarak optimistic locking mi, pessimistic locking mi tercih edilecek? (Faz 1'de her ikisi de denenip karşılaştırılacak.)
- Hold state'i Redis'te mi yoksa veritabanında mı birincil olarak tutulacak? (İkisinin tutarlılığı nasıl sağlanacak?)
- Süresi dolan hold temizliği arka plan servisi ile mi, yoksa Redis TTL event'leri ile mi yapılacak?
- Çoklu koltuk seçiminde kısmi başarısızlık nasıl ele alınacak? (5 koltuktan 4'ü alınabildi — hepsi iptal mi, kısmi kabul mü?)

---

## 11. Sonraki adım

Bu doküman onaylandıktan sonra:
1. Domain modeli ERD olarak çizilecek (varlıklar arası ilişkiler ve kardinaliteler)
2. ERD'den veritabanı şeması türetilecek (PK, FK, indeksler, version kolonu)
3. .NET solution ve katman yapısı kurulacak
4. Domain katmanından kodlamaya başlanacak
