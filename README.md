# QuickNoteApp

Windows için hızlı not alma, bildirim takibi ve Gemini CLI destekli masaüstü uygulaması.

## İndir

Normal kullanıcı için tek adım:

[QuickNoteApp-Kurulum.zip indir](https://github.com/kibrit74/QuickNoteApp/releases/latest/download/QuickNoteApp-Kurulum.zip)

Zip dosyasını çıkarın ve `QuickNoteApp-Kur.cmd` dosyasına çift tıklayın.

Kurulum dosyası:
- eski QuickNoteApp kurulumunu kaldırır,
- sertifikayı güvenilir alana ekler,
- yeni MSIX paketini kurar.

## Landing Page

Landing page dosyaları `LandingPage/` klasöründedir. GitHub Pages için aynı sayfa `docs/index.html` olarak da yayınlanabilir.

## Geliştirme

```powershell
cd QuickNoteApp
dotnet restore
dotnet run
```

Paket oluşturmak için:

```powershell
cd QuickNoteApp
.\scripts\package-msix.ps1 -Version 1.0.0.54
```