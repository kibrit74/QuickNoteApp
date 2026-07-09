# QuickNote Takvim ve Gemini Gun Planlama Tasarimi

## Hedef

QuickNote icinde kullanici takvimini kolayca gorebilsin ve Gemini CLI'a "gunumu planla" gibi bir komut verdiginde cevap, Google Takvim verileri dikkate alinarak uretilecek. Akis basit kalacak: QuickNote takvim verisini kendi Google entegrasyonu ile ceker, Gemini CLI'a sadece gerekli metin baglamini verir.

## Kapsam

- Not editorundeki hizli araclar arasina `Takvim` butonu eklenecek.
- Buton, mevcut panel/dashboard takvim gorunumunu acacak ve bugunun takvimini yukleyecek.
- Gemini niyet algilama `gunumu planla`, `bugunumu planla`, `yarinimi planla`, `takvimime gore plan yap` gibi ifadeleri takvim akisi sayacak.
- Takvim akisi secili gunun etkinliklerini, yaklasan onemli etkinlikleri, gunun notlarini ve ilgili bildirimleri Gemini CLI'a baglam olarak verecek.
- Gemini cevabi nota tek bir bolum olarak eklenecek; hata veya bos cevap nota eklenmeyecek, sadece durum metninde gosterilecek.

## Kapsam Disi

- Bu asamada MCP kurulumu yok.
- Bu asamada Google Drive dosyalarina erisim yok.
- Takvim etkinligi olusturma akisinda buyuk degisiklik yok; mevcut `Takvime Ekle` davranisi korunacak.

## Kullanici Akisi

1. Kullanici `Takvim` butonuna basar.
2. QuickNote dashboard/panel takvim alanina gecer.
3. Google bagliysa bugunun etkinlikleri listelenir.
4. Google bagli degil veya oturum suresi dolmussa sade durum mesaji gosterilir.
5. Kullanici Gemini alanina `gunumu planla` yazar.
6. QuickNote bugunun takvim verisini ve gunluk baglami toplar.
7. Gemini CLI sadece bu baglama gore kisa, uygulanabilir bir gun plani uretir.

## Teknik Tasarim

- `GeminiSearchIntent` takvim anahtar kelimelerine planlama ifadeleri eklenir.
- `SendCalendarQuestionToGeminiAsync` durum cevaplarinda erken doner; boylece hata metni nota eklenmez.
- `GeminiTaskListPrompt` veya yeni kucuk bir prompt uretici kullanilarak takvim + not + bildirim baglami daha planlama odakli verilir.
- `QuickNoteWindow.xaml` hizli araclara `Takvim` butonu ekler.
- `QuickNoteWindow.xaml.cs` icinde buton handler'i mevcut dashboard takvimini acar ve bugunu yukler.

## Test ve Dogrulama

- `GeminiSearchIntent.ShouldUseCalendar("gunumu planla")` true donmeli.
- Normal not ozeti davranisi bozulmamali.
- Takvim durum cevabi geldiğinde nota bos/hata bolumu eklenmemeli.
- Mumkunse hedefli testler `QuickNoteApp.Tests` icine eklenir.
- Tam test derlemesi mevcut WPF `obj`/calisan exe kilitlerine takilirsa hedefli gecici kontrol ile niyet ve prompt davranisi dogrulanir.

## Daha Sonra

Drive veya MCP entegrasyonu, kullanici gercekten Drive dosyalarini planlamaya katmak istediginde ikinci asama olarak ele alinacak. Bu, Google Drive izinleri ve ek hata durumlari getirdigi icin ilk surume dahil edilmiyor.
