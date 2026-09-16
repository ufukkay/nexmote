# NexMote Profesyonelleşme ve Production Readiness Planı

**Tarih:** 2026-09-09  
**Kapsam:** Güvenlik, güvenilirlik, ölçeklenebilirlik, test, dağıtım ve ürün operasyonu  
**Mevcut çalışma notu:** İnceleme sırasında çalışma ağacında kullanıcıya ait değişiklikler vardı. Bu plan kaynak dosyaları değiştirmeden hazırlanmıştır.

## 1. Mevcut Durum

NexMote; ASP.NET Core API, SQLite, SignalR, Windows LocalSystem servisi, Tray/Input Helper, WPF teknisyen istemcisi, React web konsolu ve MSI paketleme akışından oluşan kapsamlı bir uzaktan yönetim platformudur.

Mevcut doğrulama sonuçları:

- .NET testleri: 31 test başarılı.
- Web production build: başarılı.
- CI/CD akışı mevcut; .NET build/test, frontend typecheck/build, NuGet/npm audit ve secret scan çalışıyor.
- Editör tanısı: yok.
- En büyük eksik: API/SignalR/browser/Windows servis seviyesinde uçtan uca güvenlik ve davranış testleri.

### İlerleme Notu - 2026-09-09

Bu planın ilk güvenlik dilimleri uygulanmıştır:

- SignalR cihaz feed aboneliği `AnyUser` authorization ile korunuyor.
- Production `PublicUrl` HTTPS olmadan başlatılmıyor; HSTS, HTTPS redirect, güvenli origin allowlist'i ve güvenlik response header'ları aktif.
- IIS setup/deploy betikleri HTTPS binding/health kontrolü ve sınırlı SMB deployment hesabını kullanıyor; `Everyone` full-control kaldırıldı.
- Remote session launch token'ı tek kullanımlık, session sahibi kullanıcıya bağlı ve reconnect için ayrı active token kullanıyor.
- Tray ve remote input/clipboard/file/terminal akışları güvenlik profili yüklenmeden fail-closed çalışıyor.
- Komut kuyruğu atomic claim ve süresi dolmuş teslim recovery davranışına sahip.
- Production update manifest imzası zorunlu; MFA başarısız denemeleri beş denemede on dakika kilitleniyor.
- Frontend TypeScript `moduleResolution` Bundler'a taşındı; README/RULES authentication ve React sürümüyle hizalandı.
- Doğrulama: .NET solution build 0 hata/uyarı, frontend production build başarılı, 31 test başarılı.
- IIS deploy: Release artifact, production config ve imzalı `versions.json.sig` sunucu paylaşımına aktarıldı. Yerel production artifact smoke testi başarılı oldu. İlk deploy sonrası duran `NexMote` Application Pool sunucuda yeniden başlatıldı ve `https://nexmote.com/health` iki ardışık kontrolde `NexMote / ok` döndü.
- Agent saha doğrulaması: Eski agent config'inde kalan `http://192.168.0.219` adresi, HTTPS redirect sonrası `RemoteCertificateNameMismatch` oluşturuyordu. Agent config'i `https://nexmote.com` olarak güncellendi; servis yeniden başlatıldıktan sonra heartbeat `204`, SignalR Hub bağlantısı başarılı ve komut kuyruğu endpoint'i `204` döndü.
- Kalıcı istemci düzeltmesi: Yeni Agent/Windows Service/Tray binary'leri private IP veya localhost config'lerini canlı sunucuya otomatik yönlendirecek `EnforceAgentServerUrl` kuralını kullanıyor; URL regression testleri 21/21 başarılı.

Production ortamında `Updates:ManifestSignature:Required` kapalıysa servis başlamaz; trusted signer pinlemesi eksik veya imza geçersizse update manifesti `DownloadCatalog` tarafından reddedilir, fakat sağlık endpoint'i çalışmaya devam eder.

## 2. Öncelik Seviyeleri

- **P0 - Kritik:** Üretime çıkmadan önce tamamlanmalı.
- **P1 - Yüksek:** İlk güvenilir production sürümünde tamamlanmalı.
- **P2 - Orta:** Ölçek ve operasyon kalitesi için gerekli.
- **P3 - İyileştirme:** Ürün deneyimi, dokümantasyon ve bakım kalitesi.

## 3. P0 - Üretim Öncesi Güvenlik Kapısı

### P0.1 SignalR authorization ve veri izolasyonu

**Durum:** Kısmen tamamlandı. Hub feed aboneliği authentication gerektiriyor; cihaz kapsamı bazlı delta izolasyonu ve tam integration testleri ayrıca tamamlanmalıdır.

**Sorun:** `SignalingHub.SubscribeToDeviceFeed` metodu açık hub üzerinde kimlik doğrulama kontrolü olmadan cihaz telemetri grubuna bağlantı ekliyor.

**İşler:**

- SignalR hub bağlantısı için açık authentication/authorization politikası tanımla.
- İnsan kullanıcı, teknisyen ve agent bağlantılarını ayrı capability sınırlarıyla modelle.
- `devices:feed` aboneliğinde yalnızca yetkili kullanıcıların cihaz kapsamındaki verileri almasını sağla.
- AgentToken ile insan session token'ını aynı authorization akışında belirsiz bırakma.
- Yetkisiz bağlantı, yanlış cihaz grubu ve session izolasyonu testlerini yaz.

**Kabul kriterleri:**

- Anonim istemci cihaz telemetrisi alamaz.
- Teknisyen yalnızca yetkili olduğu cihaz akışını alır.
- Agent yalnızca kendi cihaz ve oturum gruplarına katılabilir.
- SignalR authorization testleri CI'da çalışır.

**Muhtemel dosyalar:** `src/NexMote.Api/Hubs/SignalingHub.cs`, `src/NexMote.Api/Program.cs`, `src/NexMote.Api/Services/SignalSessionAccess.cs`, `tests/NexMote.Tests/`

### P0.2 HTTPS ve güvenli cookie zorunluluğu

**Durum:** Uygulama, deployment ve uzak IIS Application Pool recovery tamamlandı. `https://nexmote.com/health` production smoke testi başarılı; gerçek sertifika binding doğrulaması health akışı üzerinden tamamlandı.

**Sorun:** IIS kurulum ve dağıtım betikleri HTTP portunu ve HTTP health kontrolünü temel alıyor. Yanlış proxy algısında session cookie `Secure` olmadan üretilebilir.

**İşler:**

- IIS üzerinde HTTPS binding ve sertifika kontrolü ekle.
- HTTP -> HTTPS redirect ve HSTS yapılandır.
- Forwarded headers için yalnızca güvenilir proxy listesi kullan.
- Production'da HTTPS yoksa uygulamanın başlamasını veya deployment'ın başarılı sayılmasını engelle.
- Health check'i HTTPS üzerinden doğrula.
- `Secure`, `HttpOnly`, `SameSite` cookie davranışını integration test ile doğrula.

**Kabul kriterleri:**

- Production deployment HTTP health endpoint'ini başarı kriteri olarak kullanmaz.
- Production session cookie `Secure=true` olur.
- Güvenilmeyen forwarded header ile HTTPS güveni yükseltilemez.

**Muhtemel dosyalar:** `scripts/setup-iis-server.ps1`, `scripts/deploy-iis.ps1`, `src/NexMote.Api/Auth/SessionCookie.cs`, `src/NexMote.Api/Program.cs`

### P0.3 Deployment paylaşım izinlerinin daraltılması

**Durum:** Betik seviyesi tamamlandı. Gerçek sunucuda eski `Everyone` ACL'lerinin ve NTFS izinlerinin saha doğrulaması gereklidir.

**Sorun:** Uygulama klasörü SMB üzerinden `Everyone` full-control olarak paylaşılabiliyor. Bu klasör uygulama DLL'leri, veritabanı, Data Protection anahtarları ve update dosyaları barındırıyor.

**İşler:**

- `Everyone` full-control paylaşımını kaldır.
- Deployment için ayrı servis hesabı veya imzalı artifact aktarımı kullan.
- Uygulama, veritabanı, Data Protection anahtarları ve downloads klasörlerini ayrı izin sınırlarına ayır.
- Deployment hesabının yalnızca gerekli yazma yetkisine sahip olmasını sağla.
- Paylaşım olmadan yapılabilecek bir deployment seçeneği ekle.

**Kabul kriterleri:**

- Yetkisiz yerel/ağ kullanıcısı production DLL veya update dosyası değiştiremez.
- `dpkeys` ve veritabanı deployment sırasında korunur.
- Deployment hesabı dokümante ve least-privilege olur.

**Muhtemel dosyalar:** `scripts/setup-iis-server.ps1`, `scripts/deploy-iis.ps1`, `README.md`

### P0.4 Remote session token yaşam döngüsü

**Durum:** Launch token tüketimi, kullanıcı sahipliği, active reconnect token'ı ve regression test tamamlandı.

**Sorun:** Remote session davet token'ı aktivasyondan sonra tüketilmiyor; beş dakikalık token sekiz saatlik aktif oturuma dönüşüyor.

**İşler:**

- Aktivasyonu atomic ve tek kullanımlık hale getir.
- Aktivasyonda yeni session token üret ve eski launch token'ı geçersizleştir.
- Session'ı oluşturan teknisyen kullanıcıya bağla.
- Session revoke, timeout ve bağlantı sonlandırma akışlarını ekle.
- Token replay ve eşzamanlı activation testleri yaz.

**Kabul kriterleri:**

- Aynı launch token ikinci kez kullanılamaz.
- Başka kullanıcı token ile session'a katılamaz.
- Aktif session yönetici veya teknisyen tarafından revoke edilebilir.
- Süresi dolmuş session hiçbir SignalR işlemine izin vermez.

**Muhtemel dosyalar:** `src/NexMote.Api/Services/RemoteSessionRegistry.cs`, remote session endpointleri, `src/NexMote.Api/Hubs/SignalingHub.cs`, testler

### P0.5 Security profile fail-closed davranışı

**Durum:** Tray ve hassas uzak işlemler fail-closed hale getirildi; gerçek Windows ağ kesintisi testi gereklidir.

**Sorun:** Tray güvenlik profiline ulaşamazsa mevcut kısıtlamasız davranış devam ediyor. Bu, dokümante edilen fail-closed modelle çelişiyor.

**İşler:**

- Son geçerli profil durumunu güvenli şekilde sakla.
- Profil alınamazsa korumalı işlemleri kapat veya son geçerli kısıtları uygula.
- Profil alınamama durumunu kullanıcıya ve loglara açıkça bildir.
- Ağ kesintisi, sunucu 500 ve bozuk profil cevapları için test yaz.

**Kabul kriterleri:**

- Profil alınamaması korumalı işlemleri serbest bırakmaz.
- Profil yoksa geriye dönük kısıtlamasız cihaz davranışı korunur.
- Profil geçişleri atomic ve loglanabilir olur.

## 4. P1 - Kontrol Düzlemi Güvenilirliği

### P1.1 Command queue lease ve recovery

**Durum:** Atomic claim ve süresi dolmuş teslim recovery tamamlandı. Kalıcı retry sayacı ve restart recovery gözlemi ayrıca eklenmelidir.

- Komut seçimi ile `Delivered` işaretini atomic claim/lease işlemine dönüştür.
- Lease süresi, retry sayısı ve kalıcı hata durumları ekle.
- API restart sonrası teslim edilmiş fakat tamamlanmamış komutları yeniden değerlendiren recovery servisi ekle.
- Aynı komutun iki agent tarafından yürütülmesini engelle.
- Queue concurrency testlerini SQLite üzerinde çalıştır.

**Kabul kriterleri:**

- Aynı komut eşzamanlı agent polling altında iki kez teslim edilmez.
- Agent cevap vermezse komut kontrollü biçimde retry veya timeout olur.
- API restart sonrası komut durumu kaybolmaz.

### P1.2 Audit bütünlüğü ve hassas veri redaksiyonu

**Durum:** Komut sonucu ve audit kaydı aynı transaction içinde tutuluyor. Hassas veri redaksiyonu ve audit yazım hatası için durable retry henüz tamamlanmadı.

- Komut sonucu ile audit kaydını aynı transaction içinde garanti et.
- Audit yazılamıyorsa komutu başarılı sayma veya açık bir durable retry kuyruğu kullan.
- Komut ve çıktı içindeki parola, token, API key ve credential desenlerini redakte et.
- Audit kayıtlarına kullanıcı, cihaz, session, correlation ID ve zaman bilgilerini zorunlu kıl.
- Audit kayıtlarının değiştirilemezlik ve retention politikasını tanımla.

### P1.3 Kimlik doğrulama ve yetki sertleştirmesi

**Durum:** MFA lockout, CORS allowlist ve güvenlik header'ları tamamlandı. Invite concurrency, cihaz kapsamı yetkilendirmesi ve çok boyutlu rate limitler bekliyor.

- MFA challenge için deneme sayısı ve lockout ekle.
- Invite kabulünü ve MFA doğrulamasını transaction/concurrency-safe yap.
- Login, MFA, invite ve destructive endpoint rate limitlerini kullanıcı + IP + cihaz boyutlarında uygula.
- CORS'u yalnızca yapılandırılmış origin listesiyle sınırla.
- Admin ve teknisyen cihaz kapsamı için yetki modelini açıkça belgeleyip test et.

### P1.4 Update zinciri güvenliği

**Durum:** Production manifest imza zorunluluğu tamamlandı. MSI code-signing, enrollment key rotation ve rollback saha doğrulaması bekliyor.

- Manifest imza doğrulamasını production'da zorunlu kıl.
- MSI/code-signing olmadan production artifact üretimini engelle.
- Enrollment key'i ortak paket sırrı olmaktan çıkar; kısa ömürlü veya cihaz bazlı kayıt mekanizması kullan.
- Update manifest, MSI hash'i, imza ve sürüm eşleşmesini deployment öncesi doğrula.
- Başarısız update için rollback ve eski sürüme dönme akışını test et.

## 5. P2 - Ölçeklenebilirlik ve Operasyon

### P2.1 Cihaz listesi ve telemetri ölçeği

**Durum:** Backend pagination, delta yayın altyapısı ve server metrics mevcut. Web polling aralığı 3 saniyeden 10 saniyeye çıkarıldı; tam SignalR delta-first web entegrasyonu ayrıca bekliyor.

- Cihaz listesi endpoint'ine pagination, filtreleme, sıralama ve field selection ekle.
- Web konsolundaki üç saniyelik tam liste yenilemesini kaldır.
- SignalR delta feed'i birincil canlı güncelleme yolu yap; tam refresh yalnızca manuel veya recovery durumunda çalışsın.
- Device, group, audit ve command sorguları için indeksleri ölçerek ekle.
- Büyük inventory JSON verilerini liste sorgusundan ayır.

### P2.2 Backup, restore ve SQLite operasyonu

**Durum:** Otomatik yedekleme mevcut; off-site kopya ve restore tatbikatı henüz doğrulanmadı.

- Off-site backup hedefi ve retention politikası belirle.
- Otomatik backup bütünlük/hash kontrolü ekle.
- Periyodik restore tatbikatını CI veya staging ortamında çalıştır.
- WAL/lock, deployment ve backup eşzamanlılığı için runbook hazırla.
- Data Protection anahtarları için ayrı backup ve kurtarma prosedürü tanımla.

### P2.3 Observability

**Durum:** Correlation ID ve yapılandırılmış temel loglar mevcut; metrik endpoint'i, dashboard ve alarm eşikleri henüz tamamlanmadı.

Aşağıdaki metrik ve loglar izlenebilir hale getirilmeli:

- aktif SignalR bağlantıları,
- agent reconnect sayısı ve süresi,
- komut teslim/sonuç gecikmesi,
- update başarı ve rollback oranı,
- backup sonucu,
- authentication/MFA başarısızlıkları,
- aktif alarm sayısı,
- API 4xx/5xx ve rate-limit reddi.

Tüm istemci ve API logları correlation ID ile ilişkilendirilmeli; boş `catch` blokları anlamlı loglarla değiştirilmelidir.

## 6. P3 - Test ve Release Kalitesi

### P3.1 Backend integration testleri

- API authentication ve role authorization.
- SignalR group isolation ve hub authorization.
- MFA brute-force, expiration ve concurrency.
- Invite replay/concurrency.
- Remote session replay/revoke.
- Command queue claim/retry/restart.
- Rate limiting ve CORS.
- Database migration, backup ve restore.

### P3.2 Web browser testleri

Playwright veya eşdeğer bir browser test paketi eklenmeli:

- login/logout,
- MFA akışı,
- Admin/Teknisyen görünüm farkları,
- cihaz filtreleme ve canlı telemetri,
- uzak oturum başlatma,
- komut çalıştırma ve audit görünümü,
- güvenlik profili ve cihaz grubu,
- alarm görünümü,
- güncelleme akışı,
- destructive action onayları.

### P3.3 Windows ve MSI test laboratuvarı

- LocalSystem service başlatma ve recovery.
- Kullanıcı oturumu değişimi ve kilit ekranı.
- Input Helper named pipe ACL ve süreç doğrulaması.
- UAC/SAS davranışı.
- Tray watchdog ve fail-closed profil davranışı.
- MSI fresh install, upgrade, downgrade engeli, uninstall ve rollback.
- Sessiz OTA update'in gerçek Windows makinede uçtan uca doğrulanması.

## 7. P4 - Dokümantasyon ve Ürün Olgunluğu

- `README.md`, `AGENTS.md`, `RULES.md` ve gerçek authentication mimarisini hizala.
- React 18/19, JWT/session token ve eski deployment bilgilerini güncelle.
- IIS deployment modelini ve Windows Server runbook'unu tanımla.
- Production release checklist ekle:
  - secret kontrolü,
  - TLS kontrolü,
  - migration,
  - backup,
  - signing,
  - smoke test,
  - rollback.
- API, frontend, agent, technician ve MSI sürümlerini tek kaynaktan doğrula.
- Destek kodları, kullanıcıya görünen hata mesajları ve olay geçmişi için ortak format belirle.

## 8. Uygulama Sırası

### Sprint 1 - Security Gate

- [x] SignalR authorization ve temel feed koruması
- [x] HTTPS/IIS ve secure cookie altyapısı
- [x] SMB izinlerinin daraltılması
- [x] CORS allowlist düzeltmesi
- [x] P0 regression testlerinin ilk paketi

### Sprint 2 - Session ve Agent Security

- [x] Remote session token rotation ve tek kullanımlık launch token
- [x] Security profile fail-closed
- [x] MFA lockout ve update manifest signature zorunluluğu
- [ ] Invite rate limit/concurrency ve MSI signing

### Sprint 3 - Command Control Plane

- [x] Atomic command claim/lease
- [x] Expired delivery recovery
- [x] Transactional audit
- [ ] Hassas veri redaksiyonu ve durable audit retry

### Sprint 4 - Scale ve Operations

- [x] Backend pagination, mevcut sorgu indeksleri ve server metrics
- [ ] SignalR delta-first web cihaz güncellemesi
- [ ] Backup/restore otomasyonu
- [ ] Metrics, alerts ve structured logging

### Sprint 5 - Verification

- [ ] API/SignalR integration testleri
- [ ] Browser testleri
- [ ] Windows servis ve named pipe testleri
- [ ] MSI/OTA uçtan uca testleri

### Sprint 6 - Release Readiness

- [ ] Dokümantasyon hizalaması
- [ ] Signing ve release artifact doğrulaması
- [x] Release artifact ve imzalı manifest sunucuya aktarımı
- [x] Production deployment rehearsal ve IIS Application Pool recovery
- [ ] Rollback rehearsal
- [ ] Final security ve code review

## 9. Production Readiness Çıkış Kriterleri

NexMote production-ready kabul edilmeden önce:

- [ ] P0 maddelerinin tamamı doğrulanmış olmalı.
- [ ] Kritik authentication, authorization ve SignalR testleri CI'da geçmeli.
- [ ] HTTPS ve signing olmadan production deployment başarısız olmalı.
- [ ] Remote session token replay edilememeli.
- [ ] Komut teslimi duplicate execution üretmemeli.
- [ ] Audit kaydı komut akışından kopmamalı.
- [ ] Backup restore tatbikatı başarılı olmalı.
- [ ] Browser ve gerçek Windows smoke testleri başarılı olmalı.
- [ ] Rollback prosedürü belgelenmiş ve denenmiş olmalı.
- [ ] README ve deployment runbook'ları çalışan sistemle aynı davranışı anlatmalı.

## 10. İlk Uygulanacak İş Paketi

İlk geliştirme dilimi aşağıdaki dört güvenlik düzeltmesini ve testlerini içermelidir:

1. SignalR cihaz feed authorization.
2. HTTPS/IIS ve secure cookie zorunluluğu.
3. `Everyone` SMB full-control paylaşımının kaldırılması.
4. Remote session token'ının tek kullanımlık ve kullanıcıya bağlı hale getirilmesi.

Bu dilim tamamlanmadan yeni kullanıcı özelliği eklenmemeli; önce kontrol düzleminin güvenlik sınırları ve session yaşam döngüsü güvence altına alınmalıdır.
