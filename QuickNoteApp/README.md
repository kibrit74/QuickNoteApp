# Hızlı Not & Bildirim Takibi — Proje İskeleti

## Ne yapıyor
- `Ctrl+Shift+N` → anında açılan mini not penceresi, Enter ile kaydeder.
- `Ctrl+Shift+R` → gün sonu özet ekranı: notlar checkbox ile işaretlenebilir,
  bildirimler "kimden ne geldi, kaç kez" şeklinde gruplu listelenir.
- Her şey `%AppData%\QuickNoteApp\data.db` içindeki yerel SQLite dosyasında — bulut yok.

## Klasör yapısı
```
QuickNoteApp/
  QuickNoteApp.csproj
  App.xaml / App.xaml.cs        → başlangıç, servisleri bağlar
  Models/                       → NoteItem, NotificationLogItem
  Services/
    DatabaseService.cs          → SQLite okuma/yazma
    HotkeyManager.cs            → RegisterHotKey (Win32) sarmalayıcı
    NotificationListenerService.cs → UserNotificationListener (WinRT)
    TrayIconService.cs          → sistem tepsisi ikonu ve menüsü
  Windows/
    QuickNoteWindow.xaml(.cs)   → hızlı not popup'ı
    DailyReviewWindow.xaml(.cs) → gün sonu özet ekranı
```

## Çalıştırma
Windows'ta, .NET 8 SDK kurulu olması şartıyla:
```
dotnet restore
dotnet run
```
Hızlı açılışı test etmek için (gerçek dünya koşulu, self-contained + ReadyToRun):
```
dotnet publish -c Release -r win-x64
```
Çıktı `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\QuickNoteApp.exe` altında olacak.


## Gemini CLI ilk kurulum

QuickNoteApp'in `AI Analiz`, `Dikte` ve `Plan` özellikleri bilgisayarda Gemini CLI bulursa çalışır.
İlk açılışta uygulama kısa bir Gemini rehberi gösterir. Rehberi sonradan tekrar görmek için:

`Ayarlar > Gemini CLI Kurulumu > Gemini Rehberini Aç`

En stabil kurulum yolu:
```
winget install --id OpenJS.NodeJS.LTS --source winget
npm install -g @google/gemini-cli@latest
gemini
```

`gemini` komutundan sonra ekranda `Sign in with Google` seçeneğini seçip tarayıcıda Google hesabına giriş yapın.
MSIX paketine `Tools\install-gemini-cli.ps1` yardımcısı da eklenir. Bu dosya önce Node.js/npm var mı bakar; yoksa `winget` ile Node.js LTS kurmayı dener, sonra resmi `@google/gemini-cli` paketini kurar veya günceller.

## ⚠️ En kritik nokta: bildirim yakalama paketleme ister

`UserNotificationListener` API'si (mail/WhatsApp bildirimlerini okuyan kısım),
uygulamanın bir **paket kimliğine** (package identity) sahip olmasını şart koşuyor.
Düz `dotnet publish` ile çıkan .exe bu API'yi çağırdığında `UnauthorizedAccessException`
alır — kod bunu yakalayıp sessizce devre dışı bırakacak şekilde yazıldı, yani **not alma
özelliği bundan etkilenmeden çalışmaya devam eder.**

Bildirim özelliğini gerçekten çalıştırmak için iki yoldan biri gerekiyor:

1. **MSIX paketleme projesi eklemek** (önerilen, en sağlam yol):
   Visual Studio'da çözüme sağ tık → Add → New Project → "Windows Application
   Packaging Project" → bu projeyi QuickNoteApp'e referans ver → paketleme projesini
   başlangıç projesi yap. Bu şekilde çalıştırdığında paket kimliği otomatik oluşur ve
   `RequestAccessAsync()` gerçek bir izin ekranı gösterir.

2. **Sparse package** ile unpackaged .exe'ye dışarıdan kimlik kazandırmak — daha az
   proje değişikliği ister ama `.msix` imzalama ve `Add-AppxPackage -Register` gibi
   ekstra adımlar gerektirir. MSIX yoluna göre daha kırılgan.

Vibe-coding sürecinde ilerlerken önce **not alma kısmını paketlemeden test et** (zaten
tam çalışır durumda), bildirim kısmını en sona bırakıp MSIX paketleme adımını ayrı bir
oturumda ele almanı öneririm — ikisini aynı anda debug etmeye çalışmak karışıklık yaratır.

## Sonraki adımlar (öneri sırası)
1. Not alma + gün sonu özet akışını paketlemeden test et, UI'ı beğendiğin hale getir.
2. `Resources/app.ico` ekleyip `TrayIconService.cs` içindeki `SystemIcons.Application`
   satırını gerçek ikonla değiştir.
3. MSIX paketleme projesini ekle, bildirim izni akışını test et.
4. `NotificationListenerService` içinde uygulama bazlı filtre ekle (örn. sadece Outlook
   ve WhatsApp'tan gelenleri kaydet) — şu an tüm bildirimleri yakalıyor.
5. Windows başlangıcında otomatik açılması için görev zamanlayıcı veya Startup
   klasörüne kısayol ekle.
