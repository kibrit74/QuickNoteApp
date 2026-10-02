# QuickNoteApp 🚀

Windows için hızlı not alma, akıllı bildirim takibi ve Google Gemini CLI entegrasyonlu modern masaüstü uygulaması. 

Bu proje, klavye kısayolları ile anında not almanızı, sistem bildirimlerinizi (WhatsApp, Outlook vb.) arka planda izleyip özetlemenizi ve Google Takviminizle tam senkronize çalışarak gün sonu planları hazırlamanızı sağlar.

---

## 📦 Kolay Kurulum (Acemi Kullanıcılar İçin)

Normal bir kullanıcının uygulamayı bilgisayarına kurması **tek bir adımdan** oluşur:

1. **Uygulamayı İndirin:**
   [QuickNoteApp-Kurulum.zip indirin](https://github.com/kibrit74/QuickNoteApp/releases/latest/download/QuickNoteApp-Kurulum.zip)

2. **Zip Dosyasını Çıkarın:**
   İndirdiğiniz zip dosyasına sağ tıklayın ve "Tümünü Çıkar" diyerek bir klasöre çıkartın.

3. **Kurulumu Başlatın:**
   Klasörün içindeki **`QuickNoteApp-Kur.cmd`** dosyasına çift tıklayın.
   - *Not:* Eğer mavi ekranlı Windows SmartScreen koruma uyarısı çıkarsa, **"Ek Bilgi"** seçeneğine ve ardından altta çıkan **"Yine de Çalıştır"** butonuna basın.
   - Bu kurulum dosyası eski sürümü otomatik kaldırır, gerekli güvenlik sertifikasını yükler ve uygulamayı Windows'a kurar.

4. **Çalıştırın:**
   Kurulum bittikten sonra Başlat menüsünde **QuickNoteApp** araması yaparak uygulamayı başlatabilirsiniz.

---

## 🛠️ İlk Yapılandırma ve Yapay Zeka Kurulumu

Uygulama ilk açıldığında akıllı özellikleri etkinleştirmeniz için size rehberlik eden bir **Kurulum Yardımcısı (Setup Wizard)** penceresi gösterir.

### 1. Antigravity CLI (agy) Kurulumu (Yapay Zeka Yardımcısı)
Uygulamanın sesli dikte, akıllı arama, not özetleme ve takvim analizlerini yapabilmesi için bilgisayarınızda **Antigravity CLI (`agy`)** kurulu ve yetkilendirilmiş olmalıdır:
1. Kurulum Yardımcısı ekranında **Antigravity CLI (agy)** satırındaki **"Denetle/Kur"** butonuna basın.
2. `agy` aracının sisteminizde hazır olduğunu doğrulayın.
3. Oturum açmak için bir terminal açıp `agy` komutunu çalıştırın. Yönlendirmeleri takip ederek hesabınızla giriş yapın.

### 2. Google Takvim Bağlantısı
Notlarınızı takviminize otomatik eklemek ve günlük planınızı takvim etkinliklerine göre oluşturmak için:
1. Kurulum Yardımcısı ekranında **Google Takvim** satırındaki **"Bağlan"** butonuna basın.
2. Tarayıcınızda açılan sayfada Google hesabınızla oturum açın ve takvim erişim izinlerini onaylayın.
3. `Giriş başarılı!` yazısını görünce tarayıcıyı kapatabilirsiniz. Uygulama durumu otomatik olarak yeşile dönecektir.

### 3. Mikrofon ve Windows Bildirim İzinleri
- **Mikrofon:** Sesli not alma (dikte) özelliğini kullanmak için sistem mikrofon iznini onaylayın.
- **Bildirimler:** WhatsApp ve Outlook gibi uygulamalardan gelen bildirimleri takip etmek için Windows bildirim dinleyicisi iznine onay verin.

> 📄 **Görsel Rehber:** Kurulum ve yapılandırma adımlarının ekran resimli detaylı anlatımı için [QuickNoteApp-Kurulum-Kilavuzu.pdf](file:///c:/Users/Obuzhukuk/Desktop/QuickNoteApp/QuickNoteApp-Kurulum-Kilavuzu.pdf) dosyasını inceleyebilirsiniz.

---

## 💻 Geliştiriciler İçin

Uygulamayı yerel ortamda çalıştırmak ve kod üzerinde değişiklik yapmak için:

```powershell
# Proje dizinine gidin
cd QuickNoteApp

# Bağımlılıkları geri yükleyin
dotnet restore

# Uygulamayı başlatın
dotnet run
```

### MSIX Paketi Oluşturma
Yeni bir kurulum paketi ve ZIP dosyası oluşturmak için aşağıdaki PowerShell betiğini çalıştırabilirsiniz:

```powershell
cd QuickNoteApp
.\scripts\package-msix.ps1 -Version 1.0.0.90
```
Bu betik uygulamayı publish eder, MSIX paketi haline getirir, kendi kendine imzalar ve `AppPackages` altında yeni bir `QuickNoteApp-Kurulum.zip` paketi oluşturur (içerisinde kurulum komut dosyaları, sertifika ve PDF kurulum kılavuzu dahil olarak).