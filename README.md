# xmouse

Aplikasi Windows untuk remap tombol mouse (kiri/kanan/tengah) dan scroll wheel secara global,
lewat tray icon + jendela konfigurasi.

## Fitur

- Remap klik kiri, kanan, tengah ke aksi lain (klik lain, double click, atau nonaktif).
- Contoh: klik kiri -> jadi double click; klik kanan -> jadi klik tengah; dsb.
- Remap scroll wheel: setiap "tick" scroll atas/bawah bisa dijadikan klik kiri/kanan/tengah, atau dinonaktifkan.
- Berjalan di tray (system tray), tidak mengganggu saat sedang tidak dibuka.
- Bisa diatur untuk otomatis jalan saat Windows startup.
- Konfigurasi disimpan otomatis di `%AppData%\xmouse\config.json`.

## Build lokal

Butuh .NET 8 SDK dan Windows (karena pakai WinForms + Win32 mouse hook).

```powershell
dotnet publish xmouse.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Hasil akhirnya ada di `publish/xmouse.exe` — single file, tidak perlu install .NET runtime terpisah.

## Build otomatis via GitHub Actions

Push ke branch `main` atau bikin tag `vX.Y.Z` untuk otomatis build. Cek tab **Actions**
di repo untuk download artifact `xmouse-win-x64`, atau — kalau push tag — otomatis dibuatkan
GitHub Release berisi `xmouse.exe`.

## Catatan teknis

- Menggunakan low-level mouse hook (`WH_MOUSE_LL`) via P/Invoke ke `user32.dll`.
- Klik hasil remap di-generate lewat `mouse_event` dan ditandai dengan signature khusus di
  `dwExtraInfo` supaya hook tidak memproses ulang event buatannya sendiri (mencegah infinite loop).
- Jalankan sebagai Administrator jika remap tidak berfungsi di beberapa aplikasi yang berjalan
  dengan hak elevated.
