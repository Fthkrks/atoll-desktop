# HyTranslate Desktop

Tencent Hunyuan HY-MT2 (1.8B, Q4_K_M) modeli ve yerel Ollama sunucusu üzerinde çalışan modern, akıcı ve WinUI 3 arayüzlü Windows masaüstü çeviri uygulaması.

---

## Özellikler

### 1. Metin Çevirisi (Google Translate Tarzı)
- Kaynak ve hedef dil seçimi, otomatik dil algılama (`Dili algıla`).
- Dilleri anında değiştirme (⇄) desteği.
- **Canlı Yazım Algılama (Debounce):** Yazmayı bıraktıktan 600 ms sonra otomatik çeviri başlatılır. Yeni tuş vuruşları önceki çeviri isteğini anında iptal eder.
- Elle "Çevir" düğmesi ve `Ctrl+Enter` kısayolu.
- **Akışlı Çıktı:** Yanıtlar Ollama'dan geldikçe gerçek zamanlı (token-by-token) olarak ekranda belirir.
- Karakter sayacı (örn. `123 / 5000`), tek tıkla temizleme ve panoya kopyalama.
- 720px ve üzeri pencerelerde yan yana iki kart, dar pencerelerde dikey istiflenen duyarlı (responsive) yerleşim.

### 2. Belge Çevirisi (DOCX, PPTX, XLSX, PDF, TXT, MD)
- **Orijinal Dosya Güvenliği:** Giriş dosyası asla doğrudan değiştirilmez; önce kopyalanır ve kopya üzerinde işlem yapılır.
- **Zengin Metin ve Biçimlendirme Koruması:** Paragraflardaki kalın, italik, bağlantı ve renk biçimleri etiketlenerek (`<g1>...</g1>`) dilbilgisi kurallarına göre yeniden sıralanabilir; model etiketleri bozarsa güvenli geri dönüş (fallback) devreye girer.
- **Geniş Format Desteği:**
  - **DOCX:** Başlıklar, tablolar, üstbilgi/altbilgiler, dipnotlar, sonnotlar, yorumlar, metin kutuları korunur; alan kodları (`w:instrText`), silinmiş metinler ve sekmeler dokunulmaz tutulur. Arapça, İbranice, Farsça, Urduca için bidi/RTL desteği eklenir.
  - **PPTX:** Tüm slaytlar, ana slayt düzenleri, not slaytları, gruplanmış şekiller ve tablolar korunur; slayt numarası/tarih alanları korunur.
  - **XLSX:** Paylaşılan metinler (`SharedStringTable`) ve satır içi metinler (`InlineString`) çevrilir. Formüller, sayılar, tarihler ve mantıksal hücreler asla bozulmaz. İsteğe bağlı sayfa adı çevirisi.
  - **TXT:** BOM tespiti (UTF-8, UTF-16) ve satır sonu stili (CRLF/LF) ile boş satırlar eksiksiz korunur.
  - **MD (Markdown):** Kod blokları (` ``` ` ve `~~~`), satır içi kodlar (`` `code` ``), bağlantı ve görsel URL'leri (`[metin](url)`), HTML etiketleri, tablo ayırıcıları ve YAML front-matter korunur; sadece içerik çevrilir.
  - **PDF (Overlay):** Sayfa yapısı, vektörler ve resimler korunarak metin blokları beyaz kutuyla örtülür ve Segoe UI yazı tipiyle kutuya sığacak şekilde dinamik boyutlandırma (font shrinking) ile yeniden yazılır.
- Büyük sürükle-bırak alanı, dosya bilgileri kartı, ilerleme çubuğu, iptal seçeneği ve çeviri tamamlandığında "Dosyayı aç" ile "Klasörde göster" kısayolları.

### 3. Çıktı Kayıt Yeri (Save Location)
- **Erken Belirleme:** Bir dosya seçildiği anda hedef çıktı yolu (`OutputFilePath`) otomatik olarak hesaplanır (tercih edilmiş son çıktı klasörü veya kaynak dosyanın klasörü).
- **Esnek Değiştirme:** Kullanıcı "Kayıt yeri" kartındaki "Değiştir…" butonu ile `FileSavePicker` üzerinden çıktı klasörünü ve adını dilediği gibi seçebilir.
- **Güvenli Dosya Adlandırma:** Elle seçilmeyen ve hedefte halihazırda var olan dosyalar için `dosya.tr (2).pdf`, `(3)` şeklinde otomatik benzersiz dosya adı oluşturulur (dosyalar sessizce ezilmez). Elle seçilen dosyalarda ise `FileSavePicker` onay mekanizması geçerlidir.
- **Kaynak Dosya Koruması:** Çıktı dosyasının kaynak dosya ile aynı olması engellenir. Hedef klasör mevcut değilse otomatik oluşturulur.

### 4. PDF Görsel Metinlerini Çevirme (OCR)
- **Windows Yerel OCR Entegrasyonu:** `Windows.Media.Ocr` ve `Windows.Data.Pdf` API'leri kullanılarak PDF sayfalarında gömülü raster görseller (ders slaytları, taranmış sayfalar vb.) yüksek çözünürlükte (~200 DPI) taranır ve içerideki metinler çevrilir.
- **Akıllı Çakışma Filtreleme (`OcrBlockBuilder`):** PDF'in kendi metin katmanında zaten bulunan kelimelerle %30'dan fazla örtüşen OCR satırları elenir; formül ve el yazısı parazitleri filtrelenir.
- **Blok Gruplama ve Tire Birleştirme:** İlişkili satırlar satır aralığı ve hizalama toleranslarına göre bloklarda birleştirilir; satır sonundaki tirelemeler (`algo-` / `rithm` → `algorithm`) düzeltilir.
- **Uyumlu Renk Örnekleme (`IOcrPixelSampler`):** OCR bloklarının arka plan rengi çevreleyen 3 piksellik halkadan medyan renk alınarak hesaplanır, metin rengi ise en karanlık piksellerin ortalaması ve kontrast kontrolüyle dinamik olarak belirlenir.
- **Dil Seçimi ve Kolay Ayar:** Ayarlar sayfasından OCR açılıp kapatılabilir; "Otomatik" veya sistemde yüklü Windows OCR dilleri arasından seçim yapılabilir.

### 5. Ayarlar ve Model Yönetimi
- **Model Konumu (OLLAMA_MODELS):** Modelleriniz varsayılan konum yerine başka bir sürücü veya klasörde (ör. `D:\ollama\models`) bulunuyorsa, "Model konumu" ayarını kullanarak uygulamanın bu klasörü kullanmasını sağlayabilirsiniz.
- **Yönetilen Ollama Sunucusu:** Model konumu ayarlandığında, uygulama 11434 portundaki mevcut Ollama sürecinizi etkilemeden, bağımsız bir portta (varsayılan: `11435`) `OLLAMA_MODELS` ortam değişkeniyle kendi Ollama sunucusunu otomatik olarak başlatır ve yönetir.
- **Otomatik Çalıştırılabilir Tespiti:** `ollama.exe` dosyası `%LOCALAPPDATA%\Programs\Ollama`, `%ProgramFiles%\Ollama` ve `PATH` üzerinde taranarak otomatik bulunur veya elle belirlenebilir.
- **Dinamik Model Seçimi:** Yüklü modeller sunucudan otomatik listelenir; düzenlenebilir ComboBox üzerinden doğrudan seçilebilir veya yeni model adı yazılabilir.
- **Doğrudan Klasöre İndirme:** "Modeli indir" butonu ile indirilen modeller doğrudan belirlenen model klasörüne kaydedilir.
- **Belge Çevirisi & OCR Ayarları:** PDF görsellerindeki metinleri çevirme (OCR) açma/kapama ve OCR dili seçimi.
- **Windows Job Object Güvenliği:** Yönetilen sunucu süreci Windows Job Object sınırıyla (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) korunur; uygulama kapandığında veya çökse bile sunucu süreci arka planda askıda kalmadan temizlenir.
- **Sunucu Günlüğü:** Sunucu çıktıları `%LOCALAPPDATA%\HyTranslate\ollama-server.log` dosyasında saklanır; dosya 5 MB'ı aştığında otomatik olarak temizlenir.
- Ollama sunucu adresi (klasör belirtilmediğinde varsayılan: `http://localhost:11434`) ve model adı yapılandırması.
- "Bağlantıyı test et" butonu: Ollama sunucusu ve model durumunu doğrular.
- Tema seçimi: Sistem (Varsayılan), Açık Tema, Koyu Tema.
- Ayarlar `%LOCALAPPDATA%\HyTranslate\settings.json` altında saklanır.

---

## Kurulum ve Başlatma

### 1. Ön Koşullar
- **Windows 10 (1809+)** veya **Windows 11 (x64)**
- **.NET SDK 9.0** (`dotnet --version` ile kontrol edebilirsiniz)
- **Ollama:** [ollama.com](https://ollama.com) adresinden indirin ve yükleyin.
- **Windows OCR Dil Paketi (İsteğe bağlı):** PDF görselleri için Ayarlar > Saat ve dil > Dil üzerinden ilgili dilin "Optik karakter tanıma" özelliğini yükleyebilirsiniz.

### 2. Modeli İndirme ve Ollama'yı Başlatma
Terminalinizde Ollama sunucusunu başlatın ve HY-MT2 modelini indirin:

```bash
# Ollama sunucusunu başlatın
ollama serve

# Modeli indirin (ayrı bir terminal penceresinde)
ollama pull maternion/hy-mt2:1.8b
```
*(Alternatif olarak model indirme işlemini uygulama içindeki **Ayarlar** sayfasından da tek tıkla yapabilirsiniz.)*

### 3. Uygulamayı Derleme ve Çalıştırma

```bash
# Çözümü x64 olarak derleyin
dotnet build HyTranslate.sln -c Debug -p:Platform=x64

# Uygulamayı başlatın
dotnet run --project src/HyTranslate.App -c Debug -p:Platform=x64
```

### 4. Testleri Çalıştırma

```bash
dotnet test tests/HyTranslate.Core.Tests -p:Platform=x64
```

---

## Bilinen Sınırlar (Known Limitations)

1. **PDF Çevirisi (Overlay Yöntemi):**
   - PDF dosyalarında sayfa mizanpajı, resimler ve vektörel içeriklerin bozulmaması için örtme (overlay) yöntemi kullanılmaktadır. Metin katmanı blokları beyaz kutuyla örtülürken, OCR blokları çevreleyen görsel arka plan rengiyle uyumlu dinamik renkle örtülür.
   - Orijinal metin PDF katmanının altında kalmaya devam eder.
2. **Taranmış PDF'ler ve OCR Desteği:**
   - Sayfada hiç metin katmanı yoksa veya gömülü görsel alanı %5'in üzerindeyse OCR devreye girer. OCR kapalıysa veya Windows OCR dil paketi yüklü değilse görsel metinleri atlanır ve uyarı bildirilir.
3. **Excel Sayfa Adları:**
   - Formül referanslarının bozulmasını engellemek amacıyla çalışma sayfası adlarının çevrilmesi varsayılan olarak kapalıdır.

