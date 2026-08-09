# NexMote Guvenlik Modeli

## Ilkeler

- Domain admin parolasi sunucuda veya agent tarafinda saklanmaz.
- Teknisyen kimligi MFA ve AD/Entra ID ile dogrulanir.
- Her cihaz benzersiz agent kimligi ve enrollment secret ile kaydolur.
- Her kritik aksiyon audit log'a yazilir.
- Agent kaldirma islemi tek kullanimlik server token veya local break-glass kodu ister.

## Rol Modeli

- Technician: cihaz gorur, izin verilen cihazlara baglanir, hazir aksiyon calistirir.
- Senior Technician: remote shell, dosya transferi ve ileri policy degisikligi yapabilir.
- Admin: cihaz gruplari, roller, agent policy ve yayin ayarlarini yonetir.
- Auditor: oturum ve komut loglarini gorur.

## UAC ve Credential

Teknisyen UAC ekranina kendi yetkili kullanici adi/parolasini girer. Parola loglanmaz, saklanmaz ve backend'e duz metin olarak gonderilmez. Bu alan daha sonra secure input ve session recording maskeleme politikasi ile sertlestirilecek.

