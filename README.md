# 📡 LSL Stream Viewer

Laboratuvar ve araştırma ortamlarında ağdaki tüm **LabStreamingLayer (LSL)** veri akışlarını keşfeden, sayısal sinyalleri (EEG, EMG, EKG, ivmeölçer vb.) gerçek zamanlı grafik olarak çizen ve metin/marker akışlarını zaman damgalı loglayan modern bir C# Windows masaüstü uygulaması.

---

## 🚀 Temel Özellikler

1. **Ağdaki Stream'leri Otomatik Keşfetme (Continuous Discovery)**:
   - LSL `ContinuousResolver` altyapısı sayesinde arayüzü kilitlemeden arka planda sürekli stream taraması yapar.
   - Stream Adı, Tipi, Kanal Sayısı, Örnekleme Hızı (Hz), Veri Formatı ve Hostname bilgilerini listeler.
2. **Gerçek Zamanlı Çizgi Grafiği (Real-Time Charting)**:
   - **Görünüm Modları**:
     - **☰ Ayrık Kanallar (Stacked)**: Her kanal için ayrı bir yatay şerit (EEG / polisomnografi tarzı). Çok kanallı veriler için idealdir.
     - **🔀 Üst Üste (Overlaid)**: Tüm kanalların ortak koordinat ekseninde çizildiği klasik osiloskop modu.
   - **Dinamik Otomatik Ölçekleme (Auto-Scale)**: Görüntülenen aktif penceredeki min/max değerlerine göre grafiği dinamik ve pürüzsüz ölçeklendirir.
   - **Dondur / Devam Et (Freeze/Pause)**: Sinyal tepelerini ve detaylarını incelemek için tek tıkla ekranı dondurma.
   - **Örnek Sayısı / Zaman Penceresi Ayarı**: 100, 250, 500, 1000, 2000, 5000 örnek.
   - Çok kanallı renk kodlaması ve lejant.
3. **Stream Metadata & XML İnceleme**:
   - Stream'in detaylı özelliklerini ve varsa kanalların XML etiketlerini (örn. `Alpha`, `Beta`, `Fp1`, `C3` [uV]) otomatik olarak ayrıştırır.
4. **Veri Akışı / Konsol Sekmesi**:
   - String/Marker akışları (örn. `LSLMarkerSender`) için renkli zaman damgalı olay kaydı.
   - Sayısal akışlar için sayısal değer önizlemesi.
   - Otomatik kaydırma (Auto-scroll) ve ekranı temizleme.
5. **Dahili Test Simülatörü (Test Stream Generator)**:
   - Harici bir cihaz veya program olmadan uygulamayı test etmek için tek tuşla 4 kanallı (Alpha, Beta, Theta, Delta) 100 Hz sentetik LSL stream'i başlatır.
6. **Tek Parça Taşınabilir (.exe)**:
   - `lsl.dll` ve .NET 8 çalışma zamanı dosyanın içine gömülüdür (Self-contained single-file).
   - Başka bir bilgisayarda kurulum yapmadan doğrudan çalışır.

---

## 🛠 Kullanım

1. `LSLStreamViewer.exe` dosyasını çalıştırın.
2. Sol panelde ağınızdaki LSL stream'leri listelenir.
3. Bir stream seçip **▶ Bağlan** butonuna tıklayın (veya stream'e çift tıklayın).
4. Sayısal bir stream ise **Canlı Grafik** sekmesinde sinyaller akmaya başlar.
5. Marker stream'i ise **Veri Akışı / Log** sekmesinde markerlar anında görünür.
6. Test amaçlı üst bardaki **🧪 Test Stream Başlat** butonuna basarak anında simüle EEG sinyali oluşturabilirsiniz.
