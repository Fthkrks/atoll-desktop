# Atoll for Windows

4pp Studio'nun çevrimdışı metin ve belge çeviri uygulaması. Tencent HY-MT2 1.8B Q4_K_M modeli LLamaSharp üzerinden aynı süreçte çalışır. Varsayılan backend **Built-in**'dir; Ollama veya ayrı model indirmesi gerekmez.

## Taşınabilir sürüm

ZIP'in tamamını bir klasöre çıkarın ve **Atoll.exe** dosyasını çalıştırın. Windows 10 2004 (19041) ve üzeri / Windows 11 x64 gerekir. 8 GB RAM önerilir. İndirme ve çıkarılmış klasör birlikte diskte yer kaplar; kesin boyutlar sürüm sayfasında ve `artifacts/package-info.json` dosyasında bulunur.

Site: https://atoll-one.vercel.app

Sürümler: https://github.com/Fthkrks/atoll-releases/releases

## Çeviri ve ayarlar

- Metin çevirisi yazmayı bıraktıktan 600 ms sonra başlar. Yeni yazı önceki isteği iptal eder; token'lar geldikçe sonuç gösterilir. Ctrl+Enter ile elle çeviri yapılır.
- DOCX, PPTX, XLSX, PDF, TXT ve Markdown desteklenir. Biçimlendirme `<gN>` etiketleriyle korunur. Kaynak dosya değiştirilmez; çıktı ayrı kaydedilir.
- Model bir kez yüklenir. Bağlam 4096 token, iş parçacığı sayısı fiziksel çekirdek sayısıdır. Yalnızca CPU backend'i vardır; AVX desteği otomatik algılanır.
- Ayarlar'da **Built-in / Ollama**, GGUF dosya seçicisi, yüklenme durumu ve **Test · Hello → Türkçe** bulunur. Backend değişikliği kaydedilince uygulanır.
- Model sırası: mevcut kullanıcı GGUF yolu → uygulamadaki `model/hy-mt2-1.8b-q4_k_m.gguf` → açıklayıcı hata ve dosya seçme düğmesi.
- Built-in kullanılırken Ollama başlatılmaz. Ollama URL, klasör, exe ve port ayarları yalnızca Ollama seçiliyken görünür.
- Tema ve OCR ayarları korunmuştur. PDF OCR isteğe bağlıdır ve Windows'ta yüklü OCR dil paketlerini kullanır.
- Ayarlar ve günlükler yalnızca `%LOCALAPPDATA%\HyTranslate` altına yazılır. Uygulama klasörü salt okunur olabilir. Model günlüğü: `embedded-model.log`.

## Geliştirme

.NET 10 SDK ve Windows gerekir. LLamaSharp 0.30.0'ın otomatik native CPU seçimi .NET 10 hedefini gerektirir; .NET Standard uyumluluk katmanı bu seçiciyi içermez. Önceki .NET 9 hedefi bu nedenle .NET 10'a yükseltilmiştir. Dağıtım self-contained'dır.

```powershell
dotnet build HyTranslate.sln -c Debug -p:Platform=x64
dotnet test HyTranslate.sln -p:Platform=x64
powershell -NoProfile -ExecutionPolicy Bypass -File build/package.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File build/smoke-test.ps1
```

Paketleme betiğinin `-ModelPath` parametresi kaynak modeli kopyalar; taşımaz veya değiştirmez. SHA-256 doğrulanır. Release win-x64, WindowsAppSDKSelfContained ve .NET self-contained olarak yayımlanır. Gerekli imzalı Visual C++ runtime DLL'leri `-VcRuntimeDirectory` altından alınır (varsayılan: Windows System32). ZIP'te tek üst klasör `Atoll/` bulunur. 2 GiB sınırı aşılırsa betik hata verir. Sonuç: `artifacts/Atoll-Windows-x64.zip`. `.gguf`, ZIP, bin, obj ve artifacts commit edilmez.

`Atoll.exe --selftest`, paketteki modeli yükler, "Hello, how are you?" metnini Türkçeye çevirir, sonucu standart çıktıya yazar ve başarılıysa 0 ile çıkar. `--check-cancellation` iptali ve sonrasındaki yeni isteği de doğrular. `--model C:\...\model.gguf` geliştirme için alternatif dosya seçer. Smoke-test betiği Ollama süreçlerini geçici durdurur ve yalnızca önceden çalışıyorlarsa tekrar başlatır.

## Lisanslar

`build/LICENSES` ve paketteki `LICENSES` tam lisans metinlerini içerir. LLamaSharp ve llama.cpp MIT; Tencent'in bu GGUF dosyası için güncel resmi lisansı Apache 2.0'dır. Model SHA-256 değeri resmi Hugging Face LFS kaydıyla aynıdır. Güncel lisans AB, Birleşik Krallık veya Güney Kore'yi dışlamaz. Dağıtımda lisans ve geçerli atıflar korunmalı, değiştirilmiş dosyalar belirtilmelidir. Model değiştirilmemiştir. Kaynak revizyonlar ve ayrıntılar `build/LICENSES/NOTICE.txt` içindedir.

## Bilinen sınırlar

PDF çevirisi metni sayfanın üzerine yeniden yazar; özgün metin altta kalır. Metin katmanı için renkli arka planlarda beyaz örtü görülebilir. OCR kapalıysa veya Windows OCR dili yüklü değilse görsellerdeki metin çevrilmez. Excel sayfa adları, formül referanslarını korumak için varsayılan olarak çevrilmez.

## Atoll projects

- [atoll](https://github.com/Fthkrks/atoll): Android app (Flutter + llama.cpp via llamadart)
- [atoll-desktop](https://github.com/Fthkrks/atoll-desktop): Windows app (WinUI 3 + LLamaSharp)
- [atoll-web](https://github.com/Fthkrks/atoll-web): website (Next.js), https://atoll-one.vercel.app
- [atoll-releases](https://github.com/Fthkrks/atoll-releases): Windows downloads

## License

The source code is released under the [MIT License](LICENSE). The translation model is not part of this repository. It is the Tencent Hy-MT2 1.8B GGUF model ([tencent/Hy-MT2-1.8B-GGUF](https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF)), released by Tencent under the Apache License 2.0.
