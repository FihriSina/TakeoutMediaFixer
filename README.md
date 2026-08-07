<p align="center"><img src="docs/app-icon.png" width="96" alt="Takeout Media Fixer icon"></p>

# Takeout Media Fixer

Google Takeout, telefon yedeği ve karışık fotoğraf/video klasörlerini **tek sürükle-bırak işlemiyle** güvenli bir arşive dönüştüren Windows masaüstü uygulaması.

Uygulama kaynak klasördeki dosyaları **silmez, taşımaz veya değiştirmez**. Yeni bir çıktı klasörü oluşturur ve bütün işlemleri kopyalar üzerinde yapar.

## Kullanıcı için

1. Yayınlanan `TakeoutMediaFixer-win-x64.zip` paketini indirin.
2. ZIP dosyasını bir klasöre çıkarın.
3. `TakeoutMediaFixer.exe` dosyasını çalıştırın.
4. Google Takeout veya medya klasörünü pencereye sürükleyip bırakın.
5. İşlem otomatik başlar. Bittiğinde **Çıktı Klasörünü Aç** düğmesine basın.

> İlk yayınlar kod imzalı olmayabilir. Windows SmartScreen uyarısı görürseniz yalnızca güvendiğiniz kaynaktan aldığınız paketi çalıştırın.

Çıktı normalde kaynak klasörün yanında oluşturulur. Bu konuma yazılamıyorsa uygulama otomatik olarak `Belgeler\Takeout Media Fixer` klasörünü kullanır:

```text
KaynakKlasor_Duzeltilmis_YYYYMMDD_HHMMSS
├── Tum_Resimler
├── Tum_Videolar
├── islem_raporu.csv
├── ozet.txt
└── telefona_kopyalama_rehberi.txt
```

## Otomatik yapılan işlemler

- Google Takeout `*.json` ve `*.supplemental-metadata.json` dosyalarını eşleştirir.
- Var olan EXIF/QuickTime çekim tarihlerini korur.
- Eksik tarihleri yaygın dosya adı biçimlerinden çıkarır:
  - `IMG_20240102_030405.jpg`
  - `VID_20240102_030405.mp4`
  - `Screenshot_2024-01-02-03-04-05-400_...png`
  - `Screenshot_20240102_030405_...jpg`
  - `20240102_030405.mp4`
  - `FB_IMG_1609459200123.jpg`
  - `IMG-20240102-WA0001.jpg`
  - `SampleApp_Photobooth_2024-1-2--03-04-05.png`
  - 13 haneli Unix-milisaniye dosya adları
- JSON içindeki GPS verisini uygun dosyalara yazar.
- JPEG içeriğine sahip olup yanlışlıkla `.png` adlandırılmış dosyalar gibi açık uzantı hatalarını düzeltir.
- Aynı adlı dosyaların üzerine yazmaz; sonuna güvenli sıra numarası ekler.
- Fotoğraf ve videoları ayrı klasörlerde toplar.
- Hasarlı/yazılamayan videolarda en azından Windows dosya oluşturma ve değiştirme tarihini düzeltir.
- Sonuçları ayrıntılı CSV ve özet raporuna kaydeder.

## Tarih çözümleme önceliği

1. Dosyanın mevcut EXIF/QuickTime tarihi
2. Aynı klasördeki Google Takeout JSON zamanı
3. Dosya adındaki kesin tarih ve saat
4. Dosya adındaki Unix zamanı
5. Yalnızca gün içeren WhatsApp adı

WhatsApp adında saat bulunmadığında uygulama gün kaymasını önlemek için **12:00** kullanır ve bunu raporda açıkça belirtir. Güvenilir tarih bulunamazsa tarih uydurulmaz; dosya raporda `Güvenilir tarih bulunamadı` olarak işaretlenir.

## Güvenlik ve gizlilik

- Kaynak klasör salt-okunur mantıkla ele alınır.
- Dosyalar internete yüklenmez.
- Uygulama çalışma sırasında ağ bağlantısına ihtiyaç duymaz.
- Çıktı için yeterli disk alanı yoksa kopyalama başlamadan işlem durdurulur.
- Aynı adlı hiçbir dosyanın üzerine yazılmaz.

## Sınırlamalar

- Google Photos albümleri, favoriler, açıklamalar ve yüz grupları yeniden oluşturulmaz.
- Fiziksel olarak bozuk bir MP4/MOV dosyasına dahili QuickTime tarihi yazılamayabilir; dosya sistemi tarihi yine düzeltilir.
- Hiç JSON, EXIF veya anlamlı dosya adı bulunmayan dosyalara tarih atanmaz.
- Uygulama kaynak dosyaları otomatik silmez. Kullanıcı doğrulama yaptıktan sonra eski klasörü kendisi yönetir.

## Geliştirici kurulumu

Gereksinimler:

- Windows 10 veya Windows 11
- .NET 10 SDK
- PowerShell 7 veya Windows PowerShell 5.1

ExifTool'u doğrulanmış SHA-256 ile indirin:

```powershell
./scripts/Get-ExifTool.ps1
```

Projeyi derleyin:

```powershell
dotnet restore TakeoutMediaFixer.sln
dotnet build TakeoutMediaFixer.sln --configuration Release
dotnet run --project tests/TakeoutMediaFixer.SmokeTests/TakeoutMediaFixer.SmokeTests.csproj --configuration Release
```

Taşınabilir Windows paketini oluşturun:

```powershell
./scripts/Build-Release.ps1
```

Paket `artifacts/TakeoutMediaFixer-win-x64.zip` konumunda oluşur.

## Proje yapısı

```text
src/TakeoutMediaFixer.Core   İşleme, JSON, tarih ve ExifTool altyapısı
src/TakeoutMediaFixer        WPF sürükle-bırak masaüstü arayüzü
tests/                       Harici test paketi gerektirmeyen smoke testler
scripts/                     ExifTool indirme ve release oluşturma betikleri
.github/workflows/           Otomatik build ve release iş akışları
```

## Lisans

Uygulama kaynak kodu MIT lisansı ile sunulur. ExifTool ayrı bir üçüncü taraf bileşenidir; ayrıntılar `THIRD_PARTY_NOTICES.md` dosyasındadır.
