Atoll 1.0.0 for Windows x64 - 4pp Studio
https://atoll-one.vercel.app

Unzip the entire Atoll folder, then run Atoll.exe. Do not run it inside the ZIP.
No installer, Ollama, .NET download, model download, or administrator access is required.
Windows 10 version 2004 (build 19041) or newer / Windows 11, x64.
8 GB RAM recommended. Keep enough disk space for both the ZIP and extracted folder;
the exact sizes are listed on the release page. After extraction the ZIP can be deleted.
Optional PDF OCR uses Windows' installed OCR language packs.

Built-in translation works offline. CPU instruction support is detected automatically.
Settings, model status logs and optional Ollama logs are written only under
%LOCALAPPDATA%\HyTranslate. The app folder can be read-only.
Settings > Backend: Built-in / Ollama switches translation engines.
Settings > Model dosyasi sec... lets you choose another local GGUF file.
An existing selected file takes priority; otherwise the bundled model is used.
Test in Settings translates Hello into Turkish.
Diagnostics: Atoll.exe --selftest (prints a Turkish translation and exits 0 on success).

MODEL AND THIRD-PARTY LICENSE NOTICES
This distribution includes the unmodified Tencent HY-MT2-1.8B Q4_K_M GGUF model.
Copyright (C) 2026 Tencent. All rights reserved.
Official model: https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF
Model SHA256: dc5f44fcf1fa496ee7ad725982c0c8c553a4de00259b53af84c4b89fb0c06699
The current official license for these exact weights is Apache License 2.0.
It has no EU, UK, or South Korea exclusion. Older Tencent community license
versions had territorial restrictions; they are not the current license for this asset.
Redistribution requires a copy of the license, retention of applicable attribution
and NOTICE files, and prominent notices if files are modified. We do not modify the model.
No upstream NOTICE file is present in the official GGUF repository at the pinned revision.
See LICENSES\Tencent-HY-MT2-Apache-2.0.txt for the full license and
LICENSES\NOTICE.txt for the pinned sources and redistribution details.

LLamaSharp 0.30.0 and llama.cpp are MIT-licensed; full texts are in LICENSES.
.NET and other open-source components retain their own licenses and notices.
Microsoft Windows App SDK and Visual C++ Runtime components retain Microsoft's
license terms, included in LICENSES. They support Atoll and are not standalone products.
Use of the included Microsoft components is subject to those terms.
The app is provided without warranty. Atoll is not endorsed by Tencent or Microsoft.
