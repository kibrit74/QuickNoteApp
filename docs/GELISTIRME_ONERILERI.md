# QuickNoteApp Geliştirme Önerileri

Tarih: 2026-07-05

Bu dosya, QuickNoteApp için paketleme, ilk kurulum sihirbazı, Gemini CLI hazırlığı ve eklenmesi gereken araç/eklenti özellikleri için öneri raporudur.

## Net Öneri

QuickNoteSetup.exe yapalım.

İçeride şu akış olsun:

1. QuickNoteApp MSIX kurulumu
2. Gemini CLI hazırlığı
3. İlk açılış kurulum sihirbazı

İlk açılış ekranı şöyle olmalı:

| Kontrol | Durumlar | Kullanıcıya gösterilecek işlem |
| --- | --- | --- |
| Gemini CLI durumu | Kurulu / Eksik | Eksikse `Kur` |
| Gemini giriş durumu | Giriş yapılmış / Giriş gerekli | Gerekliyse `Bağlan` |
| Google bağlantısı | Bağlı / Bağlan | Eksikse `Bağlan` |
| Bildirim izni | Açık / İzin ver | Kapalıysa `İzin ver` |

Kullanıcı sadece eksik olanların yanında `Bağlan`, `Kur` veya `İzin ver` görmeli. Her şey tamamsa uygulama direkt ana ekrana girmeli.

## Neden Bu Yol Daha Mantıklı?

Mevcut projede MSIX zaten var ve bildirim dinleme gibi Windows kimliği isteyen işler için önemli. Ama MSIX tek başına Node, npm veya Gemini CLI gibi dış araçları sessizce kurmak için rahat bir yol değil. Bu yüzden en pratik çözüm:

- Dışarıdan kullanıcıya tek dosya: `QuickNoteSetup.exe`
- İçeride kontrollü akış: MSIX kurulumu + Gemini hazırlığı + ilk açılış kontrolü
- Uygulama içinde sade kurulum ekranı: eksik neyse sadece onu göster

Bu yapı kullanıcı için tek tık gibi hissedilir, bizim için de hata ayıklaması daha kolay olur.

## Paketleme Mimarisi

Önerilen yapı:

```text
QuickNoteSetup.exe
  ├─ QuickNoteApp.msix
  ├─ Kurulum yardımcısı
  ├─ Node kontrolü veya portable Node
  ├─ Gemini CLI kontrolü
  └─ QuickNoteApp başlatma
```

Kurulumdan sonra uygulama açılınca `SetupWizardWindow` gibi küçük bir ekran çalışmalı. Bu ekran kontrolleri sırayla yapmalı:

1. Gemini CLI bulunuyor mu?
2. Gemini komutu çalışıyor mu?
3. Gemini Google hesabıyla giriş yapmış mı?
4. QuickNote Google bağlantısı var mı?
5. Bildirim izni açık mı?
6. Mikrofon izni açık mı?
7. Outlook masaüstü uygulaması var mı?

Her kontrol ayrı küçük servis olmalı. Örneğin:

- `GeminiCliHealthService`
- `GoogleConnectionHealthService`
- `NotificationPermissionService`
- `MicrophonePermissionService`
- `OutlookHealthService`

Bu sayede ileride yeni kontrol eklemek kolay olur.

## Gemini CLI Kurulum Stratejisi

### Önerilen stabil yol

İlk sürümde şu yolu öneriyorum:

1. Bilgisayarda Node var mı kontrol et.
2. Yoksa kullanıcıya Node kurulumunu başlat.
3. Gemini CLI yoksa `npm install -g @google/gemini-cli` çalıştır.
4. Sonra kullanıcıyı Gemini login ekranına yönlendir.

Artısı: Basit, güncel, resmi kullanım şekline yakın.

Eksisi: Node kurulumu bazı bilgisayarlarda yetki isteyebilir.

### Daha kontrollü ama daha ağır yol

Portable Node + Gemini CLI paket içine alınabilir.

Artısı: Kullanıcı sistemindeki Node'a bağımlılık azalır.

Eksisi: Paket büyür, Gemini CLI güncellemesini biz takip ederiz.

### Benim önerim

Önce basit yol ile başlayalım. Sonra kurulum sorunları çoğalırsa portable Node yoluna geçelim.

## İlk Açılış Sihirbazında Mutlaka Olması Gerekenler

### 1. Durum kartları

Kullanıcıya teknik hata yerine net durum gösterilmeli:

- Gemini CLI: Kurulu
- Gemini giriş: Giriş gerekli
- Google bağlantısı: Bağlı
- Bildirim izni: İzin gerekli
- Mikrofon izni: Açık
- Outlook: Hazır

### 2. Tek ana işlem

Ekranda gereksiz buton kalabalığı olmamalı. Her satırda sadece eksik işlem varsa buton görünmeli.

Örnek:

```text
Gemini CLI        Eksik          Kur
Gemini giriş      Gerekli        Bağlan
Google            Bağlı
Bildirim izni     Açık
```

### 3. Sağlık testi

Kurulumdan sonra uygulama küçük bir test yapmalı:

- `gemini --version` çalışıyor mu?
- Gemini basit bir soruya cevap verebiliyor mu?
- Google token süresi dolmuş mu?
- Bildirim izni okunabiliyor mu?

### 4. Sorun çözme ekranı

Bir şey bozulduğunda kullanıcıya düz hata değil, çözüm gösterilmeli:

- Gemini bulunamadı: `Gemini CLI kur`
- Gemini giriş yok: `Gemini girişini aç`
- Bildirim izni kapalı: `Windows bildirim ayarlarını aç`
- Mikrofon kapalı: `Windows mikrofon ayarlarını aç`

## Mutlaka Eklenmesi Gereken Araç ve Eklenti Özellikleri

### 1. Gemini Health Check

Uygulama Gemini'nin sadece kurulu olup olmadığını değil, gerçekten kullanılabilir olup olmadığını kontrol etmeli.

Mutlaka kontrol edilecekler:

- `gemini` komutu bulunuyor mu?
- Sürüm bilgisi alınabiliyor mu?
- Model parametresi çalışıyor mu?
- Kullanıcı giriş yapmış mı?
- Basit metin isteği cevap dönüyor mu?
- Ses dosyası ile dikte testi çalışıyor mu?

Öncelik: Çok yüksek.

### 2. Kurulum Günlüğü

`QuickNoteSetup.exe` tüm adımları log dosyasına yazmalı.

Örnek dosya:

```text
%AppData%\QuickNoteApp\Logs\setup.log
```

Bu olmazsa kullanıcıda kurulum bozulduğunda neyin patladığını anlamak zor olur.

Öncelik: Çok yüksek.

### 3. Uygulama İçi Tanılama Ekranı

Ayarlar içinde küçük bir `Tanılama` ekranı olmalı.

Göstereceği bilgiler:

- Uygulama sürümü
- MSIX kurulu mu?
- Veritabanı yolu
- Gemini CLI yolu
- Gemini sürümü
- Google bağlantı durumu
- Bildirim izni
- Mikrofon izni
- Son 20 hata

Bir de `Raporu kopyala` düğmesi olmalı. Kullanıcı hata attığında doğrudan bu metni gönderebilir.

Öncelik: Çok yüksek.

### 4. Veritabanı Bakım Aracı

Notlar, bildirimler ve günlük özetler SQLite içinde büyüyecek. Şunlar olmalı:

- Veritabanı yedeği al
- Veritabanını dışa aktar
- Bozuk kayıtları kontrol et
- Eski kayıtları temizle
- Arama indeksini yenile

Öncelik: Yüksek.

### 5. Akıllı Arama Katmanı

Kullanıcı ayrıca `semantik ara` düğmesine basmamalı. Normal soru kutusuna yazdığı şeyden niyeti uygulama anlamalı.

Örnekler:

- `Ömer Tamdoğan 3 Temmuzda kaç mail atmış?`
- `Bugün WhatsApp'ta Ayşe ne yazmış?`
- `Geçen hafta en çok kimden bildirim gelmiş?`
- `Bu ay kaç duruşma notu aldım?`

Uygulama önce veritabanından ilgili kayıtları çekmeli, sonra Gemini'ye sadece gerekli bağlamı vermeli.

Öncelik: Yüksek.

### 6. Bildirim Kaynağı Yönetimi

Şu an sadece Outlook ve WhatsApp iyi bir başlangıç. Ama ileride ayar ekranında kaynaklar yönetilebilmeli.

Öneri:

- Outlook: Açık
- WhatsApp: Açık
- Gmail: Kapalı
- Telegram: Kapalı
- Teams: Kapalı
- Özel uygulama ekle

Varsayılan yine sade kalmalı: Outlook + WhatsApp.

Öncelik: Orta.

### 7. Model Profilleri

Kullanıcı model seçimiyle uğraşmamalı ama uygulama kendi içinde profil kullanmalı:

- Dikte: `gemini-2.5-flash`
- Hızlı özet: `gemini-2.5-flash`
- Derin analiz: `gemini-2.5-pro`
- Veritabanı soruları: önce SQL, sonra gerekirse Gemini

Öncelik: Yüksek.

### 8. Dikte Kalite Katmanı

Dikte çıktısı sadece transkripsiyon olmamalı. Türkçe hataları düzeltmek için ikinci küçük temizlik adımı olmalı.

Kurallar:

- Sadece konuşulan metni al
- Gemini açıklamalarını temizle
- Tırnak içi metin varsa sadece onu al
- Türkçe karakterleri koru
- Hukuk terimlerini bozma

Öncelik: Yüksek.

### 9. Güncelleme Mekanizması

Tek tık kurulumdan sonra uygulamayı güncel tutmak için basit bir güncelleme kontrolü gerekir.

İlk sürüm için:

- Uygulama açılırken yeni sürüm var mı kontrol et
- Varsa kullanıcıya bildir
- İndir ve kur akışı başlat

Öncelik: Orta.

### 10. Geri Alma ve Güvenli Kurulum

Kurulum yarıda kalırsa sistem kötü durumda kalmamalı.

Gerekli davranış:

- MSIX kurulmadıysa Gemini kurulumuna geçme
- Gemini kurulumu bozulursa uygulama yine açılsın
- Kullanıcı isterse `Kurulumu onar` çalışsın
- Eski veri silinmesin

Öncelik: Çok yüksek.

### 11. UTF-8 ve Türkçe Karakter Kontrolü

Bu proje için ayrı bir kalite kapısı olmalı.

Her paketlemeden önce:

- Kaynak dosyalarda bozuk Türkçe karakter taraması
- XAML metinlerinde bozulma taraması
- PowerShell scriptlerinde UTF-8 kontrolü
- Test çıktısında Türkçe karakter kontrolü

Öncelik: Çok yüksek.

### 12. Outlook Sağlık Kontrolü

Mail gönderme tarafında Outlook seçildiyse tarayıcı açılmamalı. Bunun için uygulama Outlook durumunu baştan bilsin.

Kontroller:

- Masaüstü Outlook kurulu mu?
- COM otomasyonu çalışıyor mu?
- Taslak mail açılabiliyor mu?
- Hata olursa kullanıcıya net mesaj veriliyor mu?

Öncelik: Yüksek.

## Öncelik Sırası

İlk etapta şunları yapalım:

1. `QuickNoteSetup.exe` iskeleti
2. Kurulum log dosyası
3. MSIX kurulum adımı
4. Gemini CLI kontrol ve kurulum adımı
5. İlk açılış sihirbazı
6. Gemini giriş kontrolü
7. Google bağlantı kontrolü
8. Bildirim ve mikrofon izin kontrolü
9. Tanılama ekranı
10. UTF-8 kalite kontrol scripti

İkinci etap:

1. Veritabanı bakım aracı
2. Akıllı arama katmanını güçlendirme
3. Bildirim kaynağı yönetimi
4. Model profilleri
5. Güncelleme kontrolü

Üçüncü etap:

1. Portable Node seçeneği
2. Kurulumu onar ekranı
3. Gelişmiş hata raporu
4. Daha kapsamlı semantik arama

## Basit Uygulama Planı

### Aşama 1: Hazırlık Kontrolleri

Yeni servisler:

- `SetupStatusService`
- `GeminiCliHealthService`
- `GoogleConnectionHealthService`
- `PermissionHealthService`
- `SetupLogService`

Bu servisler sadece durum okuyacak. UI'ı şişirmeden önce temel mantık test edilecek.

### Aşama 2: İlk Açılış Sihirbazı

Yeni pencere:

- `SetupWizardWindow`

Bu pencere uygulama açılırken çalışacak. Her şey hazırsa görünmeden kapanacak ve ana ekran açılacak.

### Aşama 3: Setup EXE

Yeni klasör:

```text
Installer/
```

İçerik:

- Bootstrapper proje dosyası
- MSIX paketini çağıran kurulum adımı
- Gemini CLI hazırlık adımı
- Loglama

### Aşama 4: Doğrulama

Kontroller:

- Temiz bilgisayarda kurulum
- Node olmayan bilgisayarda kurulum
- Gemini giriş yapılmamış bilgisayarda kurulum
- Bildirim izni kapalı bilgisayarda kurulum
- Türkçe karakter bozulma taraması

## Dikkat Edilecek Riskler

### Google girişini tamamen sessiz yapmak mümkün değil

Kullanıcı Google hesabına tarayıcıdan izin vermek zorunda. Biz sadece bu akışı kolaylaştırabiliriz.

### MSIX tek başına her şeyi kurmak için uygun değil

MSIX uygulama kimliği ve Windows entegrasyonu için iyi. Ama Node/Gemini gibi dış araçlar için `QuickNoteSetup.exe` daha pratik.

### Gemini CLI güncellenebilir bir dış bağımlılık

Gemini CLI değişirse uygulamanın dikte veya arama akışı etkilenebilir. Bu yüzden sağlık testi ve tanılama ekranı şart.

### Türkçe karakter kalitesi ayrı korunmalı

PowerShell ve bazı terminal çıktıları Türkçe karakterleri bozuk gösterebilir. Kaynak dosyalar mutlaka UTF-8 olarak yazılmalı ve paketleme öncesi taranmalı.

## Kaynak Notları

- Gemini CLI resmi GitHub sayfası: https://github.com/google-gemini/gemini-cli
- Gemini CLI Google hesabı ile giriş dokümanı: https://geminicli.com/docs/get-started/authentication/
- Microsoft App Installer dokümanı: https://learn.microsoft.com/windows/msix/app-installer/how-to-create-appinstaller-file
- Microsoft Windows App SDK deployment dokümanı: https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-unpackaged-apps
- MSIX custom action kısıtı tartışması: https://techcommunity.microsoft.com/discussions/msix-discussions/custom-action-with-elevated-privilege-in-msix/1748298

## Son Karar

En stabil ve pratik yol:

```text
QuickNoteSetup.exe
  → QuickNoteApp MSIX kur
  → Gemini CLI hazırla
  → Uygulamayı aç
  → İlk açılış sihirbazında eksikleri tamamlat
  → Her şey tamamsa direkt ana ekrana gir
```

Bu yapı hem kullanıcı için sade, hem bizim için bakımı kolay.
