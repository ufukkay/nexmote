# NexMote Guvenlik Modeli

## Su anki durum (2026-08-11 itibariyla)

Asagidaki "Rol Modeli" bolumu hedef mimariyi anlatir; bugun kodda teknisyen
uclarini (`/api/devices`, `/api/remote-sessions`, `/api/settings`,
`/api/downloads/generate`) koruyan ayri bir kimlik dogrulama katmani yoktur —
sunucuya agdan erisebilen herkes bu uclari kullanabilir. Bu bilincli bir
tercih: NexMote su an kapali/guvenilir bir LAN'da, tek teknisyen/ekip
tarafindan calistirilmak uzere tasarlaniyor. "Technician / Senior Technician /
Admin / Auditor" rolleri, AD/Entra ID SSO ve MFA henuz implemente edilmedi
(Faz 4). Agent tarafinda enrollment anahtari ve heartbeat/agent token kontrolu
degismeden devam ediyor. NexMote genel internete acilacaksa bu uclarin onune
bir kimlik dogrulama katmani (VPN, reverse-proxy auth veya SSO) eklenmelidir.

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

