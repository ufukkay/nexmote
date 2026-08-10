# NexMote Guvenlik Modeli

## Su anki durum (2026-08-09 itibariyla)

Asagidaki "Rol Modeli" bolumu hedef mimariyi anlatir; bugun kodda gercekten var olan
tek katman, backend'in ilk baslatmada urettigi paylasilan **Teknisyen Erisim
Anahtari**dir (`X-Technician-Key` HTTP basligi, bkz. ana `README.md`). Bu anahtar
tum teknisyenler icin ortaktir ve rol ayrimi yapmaz — "Technician / Senior
Technician / Admin / Auditor" rolleri, AD/Entra ID SSO ve MFA henuz
implemente edilmedi (Faz 4). Kisa vadede kapatilan gercek bir acik: enrollment
anahtari kontrolu daha once her kosulda `dev-enrollment-key`'i de kabul
ediyordu, bu bypass kaldirildi.

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

