# Architecture

## Akış

1. Kullanıcı klasörü sürükler.
2. Uygulama bütün dosyaları reparse point izlemeden tarar.
3. Takeout JSON dosyaları dizine alınır.
4. Dosya uzantısı ve imzası birlikte değerlendirilerek medya belirlenir.
5. Her medya için aday tarih ve GPS verisi çözülür.
6. Medya yeni çıktı klasörüne kopyalanır; çakışan adlara sıra numarası eklenir.
7. ExifTool ile var olan EXIF/QuickTime tarihleri toplu okunur.
8. Var olan güvenilir tarih korunur; eksik tarih JSON veya dosya adından yazılır.
9. Windows dosya oluşturma/değiştirme tarihleri de eşitlenir.
10. CSV ve metin raporu oluşturulur.

## Güven varsayımları

- Kaynak klasör değiştirilemez kabul edilir.
- Çıktı kaynak klasörün yanında oluşturulur.
- Güvenilir kanıt yoksa tarih yazılmaz.
- Tarih-only veri için 12:00 teknik yer tutucusu kullanılır ve raporlanır.
- Meta veri yazma başarısız olsa bile kopyalanan dosya korunur.

## ExifTool entegrasyonu

Uygulama ExifTool'u `-stay_open` kipinde tek süreç olarak çalıştırır. Böylece binlerce dosyada her dosya için yeni süreç açma maliyeti oluşmaz. Önce toplu JSON okuması ile mevcut tarih alanları belirlenir, sonra yalnızca eksik alanlar yazılır.
