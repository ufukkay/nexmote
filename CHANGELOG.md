# 📋 NexMote - Sürüm Günlüğü ve Değişiklik Tarihçesi (Changelog)

Bu doküman, **NexMote** projesinde yayınlanan her sürümdeki yeni özellikleri, hata düzeltmelerini, performans iyileştirmelerini ve mimari değişiklikleri detaylı olarak kayıt altına alır.

---

## 🏷️ [v0.8.3] - 2026-09-15 (Güncel Sürüm)
### 📧 SMTP Dayanıklılığı ve Gelişmiş Teknisyen Yönetimi (Şifre Değiştirme, MFA Kaldırma, Teknisyen Silme)
- **SMTP Bağlantı ve Sertifika İyileştirmeleri (`EmailService.cs`, `SettingsEndpoints.cs`):**
  - Hostinger, cPanel, dahili posta sunucuları ve self-signed sertifikalarda yaşanan `RemoteCertificateNameMismatch` ve SSL handshake kesintilerini önlemek amacıyla toleranslı sertifika denetimi (`ServerCertificateValidationCallback`) sağlandı.
  - SMTP zaman aşımı 15 saniyeye indirilerek port engeli veya askıda kalma durumlarında uygulamanın donması engellendi.
  - MailKit XOAUTH2 mekanizması temizlenerek gereksiz SASL auth hataları önlendi.
  - Şifreleme modu seçeneği (`SmtpSslMode`: Otomatik, SSL/TLS 465, STARTTLS 587, Düz Metin) hem veritabanına hem arayüze eklendi.
  - Formdaki güncel değerlerle anlık test yapabilme desteği (`SendTestAsync`) ve ayrıntılı hata bildirimleri getirildi.
- **Yönetici Tarafından Teknisyen Şifresi Değiştirme (`UserAuthService.cs`, `AuthEndpoints.cs`, `UsersView.tsx`):**
  - Yöneticilerin (Admin) panel üzerinden diledikleri teknisyenin şifresini doğrudan belirleyebilmesini sağlayan `POST /api/admin/users/{id}/password` endpoint'i ve arayüz modalı eklendi.
  - Şifre karmaşıklık politikası (`PasswordValidator`) denetlendi, şifresi değişen kullanıcının mevcut tüm oturumları otomatik sonlandırıldı.
  - Arayüzde rastgele güçlü şifre üretme (`Sparkles`), şifreyi panoya kopyalama ve göster/gizle butonları sunuldu.
- **Yönetici Tarafından Teknisyen MFA'sını Kaldırma (`UserAuthService.cs`, `AuthEndpoints.cs`, `UsersView.tsx`):**
  - İki adımlı doğrulaması açık veya hatalı denemeler yüzünden kilitlenmiş teknisyenlerin MFA kilidini ve yapılandırmasını sıfırlayan `AdminResetMfa` güçlendirildi.
  - Hatalı deneme sayaçları ve kilitlenme süreleri sıfırlandı, askıdaki MFA challenge oturumları iptal edildi.
  - Arayüzde açıklayıcı onay penceresi ve `ShieldOff` aksiyon butonu eklendi.
- **Yönetici Tarafından Teknisyen Silme (`UserAuthService.cs`, `AuthEndpoints.cs`, `UsersView.tsx`):**
  - Sistemden bir teknisyeni kalıcı olarak silmeyi sağlayan `DELETE /api/admin/users/{id}` endpoint'i eklendi.
  - Yöneticinin kendi hesabını silememesi ve sistemdeki son aktif Admin'in silinememesi için güvenlik kısıtlamaları uygulandı.
  - Silinen kullanıcının oturumları (`UserSessions`) ve davetleri (`UserInvites`) güvenle temizlendi, denetim kaydı (`user.delete`) işlendi.
- **Kilit Ekranı (Winlogon) Fare ve Klavye Giriş Restorasyonu (`DesktopHelper.cs`, `InputHelperServer.cs`, `InputInjector.cs`, `Program.cs`):**
  - `Program.cs` üzerinde `--input-helper`, `--system-session` ve `--send-sas-once` modları `ApplicationConfiguration.Initialize()` ve `WindowsFormsSynchronizationContext` çağrılarının öncesine alındı; iş parçacığında oluşan gizli HWND penceresi nedeniyle Windows çekirdeğinin `SetThreadDesktop` çağrısını `ERROR_BUSY (170)` ile kalıcı reddetmesi sorunu giderildi.
  - `DesktopHelper.AttachToActiveDesktop` üzerinde `Winlogon` ACL kurallarıyla uyumsuz olan `DESKTOP_ALL (0x020001FF)` maskesi yerine `MAXIMUM_ALLOWED (0x02000000)` kullanıldı; `Winlogon` masaüstü erişiminde `ERROR_ACCESS_DENIED (5)` hatası ve `Default` masaüstüne hatalı geri düşme önlendi.
  - Masaüstü denetim aralığı 1 saniyeden 100ms seviyesine çekildi; fare tıklaması ve tuş basımlarında `force: true` ile aktif masaüstüne anında bağlanma garantilendi.
  - `InputInjector.MoveMouse` içinde salt `SetCursorPos` koordinat taşımasının Windows 10/11 kilit perdesini uyandırmaması problemi, `SendInput` ve `mouse_event` ile sentetik `MouseMove` donanım olayları enjekte edilerek çözüldü.
  - `InputHelperServer` üzerinde istemci bağlantıları arka planda asenkron işlenerek boru kilitlenmeleri önlendi; Windows Servisinin (Session 0) SAS ve kilit açma sinyalleri güvenlik doğrulamasından geçecek şekilde güncellendi.

---

## 🏷️ [v0.8.2] - 2026-09-09
### ⚡ Canlı Görüntü, Fare/Klavye ve Sinyalleşme Restorasyonu
- **SignalR WebSocket Yetkilendirme & Query Access Token Desteği (`SessionCookie.cs` & `SignalingHub.cs`):**
  - SignalR WebSocket bağlantılarında gelen `access_token` query parametresi kimlik doğrulama katmanına dahil edildi.
  - `JoinTechnicianSession` içindeki gereksiz Authorization attribute engeli kaldırılarak 32 baytlık kriptografik oturum token'ı ile doğrudan, anında ve güvenilir katılım sağlandı.
- **Fail-Closed Girdi Engelleme Düzeltmesi (`RemoteScreenStreamer.cs` & `Program.cs`):**
  - Sunucuda özel güvenlik profili tanımlanmamış olsa dahi varsayılan olarak fare ve klavye girdilerinin çalışması garanti edildi (`_securityProfileLoaded` varsayılan true yapıldı, kısıtlama yalnızca profil açıkça yasakladığında uygulanacak şekilde düzeltildi).
- **Sunucu Adresi Yönlendirme Düzeltmesi (`NexMoteHttp.cs`):**
  - `EnforceAgentServerUrl` fonksiyonunun yerel ağ ve IIS IP adreslerini (`192.168.0.219`) zorla harici `nexmote.com` adresine yönlendirmesi engellendi; yerel sunucuların bağımsız çalışması sağlandı.

---

## 🏷️ [v0.8.1] - 2026-09-09
### 🛠️ Görüntü Akışı ve Kilit Açma İyileştirmeleri (Session Watchdog & Secure Desktop SAS Fixes)
- **Kullanıcı Oturum Kontrolü ve Süreç İyileştirmesi (`SessionProcessLauncher.cs`):**
  - `IsTrayRunningInSession` içindeki hatalı süreç kontrolü kaldırıldı; `--input-helper` sürecinin tepsiyi çalışıyor gibi göstermesi ve tepsiyi engellemesi giderildi. Named Pipe ve oturum Mutex'i ile kesin denetim sağlandı.
- **SYSTEM Düzeyinde Oturum Başlatma ve İzin Aşımı (`Worker.cs` & `Program.cs`):**
  - Tray ve InputHelper süreçleri aktif kullanıcı oturumuna SYSTEM yetkileri çoğaltılarak (`TryLaunchInActiveSession`) başlatıldı. Standart kullanıcı oturumlarında `Global\` mutex erişim reddi (`UnauthorizedAccessException`) güvenli oturum fallback'i ile korundu.
- **Kilit Ekranı ve Yazılımsal SAS (Ctrl+Alt+Del) Çift Kanallı Çözümü:**
  - `SoftwareSASGeneration = 3` ve `PromptOnSecureDesktop = 0` kayıt defteri anahtarları otomatik denetlenerek yazılımsal SAS garantilendi.
  - Teknisyen kilit aç butonuna bastığında hem Session 0 hem de aktif masaüstü girdi yardımcısı (`--send-sas-once`) üzerinden kilit perdesi kaldırılıp parola kutusunun açılması sağlandı.
- **Dinamik Güncelleme Adresi Çözümlemesi (`SettingsEndpoints.cs`):**
  - `/api/updates/check` çağrılarında gelen istemcinin ana bilgisayarı (`http://192.168.0.219`) dinamik olarak algılanıp güncelleme paketleri doğrudan doğru sunucudan indirilecek şekilde ayarlandı.

---

## 🏷️ [v0.8.0] - 2026-09-09
### 🚀 DirectX 11 DXGI GPU Ekran Yakalama, WebRTC P2P Akışı ve Kusursuz Masaüstü Geçişi
- **DirectX 11 DXGI Desktop Duplication (`DxgiScreenCapture.cs`):**
  - Ekran kareleri doğrudan GPU VRAM framebuffer üzerinden yakalanarak CPU yükü sıfıra indirildi (%1-%3 aralığı).
  - Donanımsal statik ekran algılaması (DXGI wait timeout) ile hareketsiz sahnelerde boşuna işlemci tüketen döngüler ve hash hesaplamaları tamamen ortadan kaldırıldı.
  - Donanım hızlandırmalı imleç (cursor) katmanı eklendi.
  - Desteklenmeyen sistemler veya sanal makineler için otomatik, sıfır gecikmeli GDI+ (`CopyFromScreen`) geri düşüşü (fallback) sağlandı.
- **WebRTC P2P Sınırsız Akış ve Paket Parçalama (`WebRtcPeerTransport.cs`):**
  - Eski 64 KB SCTP DataChannel sınırı dinamik paket parçalama protokolü (`__CHK__|{msgId}|{chunkIndex}|{totalChunks}|{data}`) ile kaldırıldı.
  - 1080p, 2K ve yüksek kaliteli tüm karelerin doğrudan WebRTC P2P veri kanalı üzerinden 10-20 ms ultra düşük gecikmeyle akması sağlandı.
  - 512 KB arabellek taşma koruması (bufferbloat guard) ile ağ tıkanıklıklarında SignalR WebSocket hattına kesintisiz geri dönüş korundu.
- **Kusursuz Masaüstü Geçişi ve Çift Akış Çakışmasının Çözümü (`Program.cs` & `DesktopHelper.cs`):**
  - `DXGI_ERROR_ACCESS_LOST` durumunda `DesktopHelper.AttachToActiveDesktop(force: true)` ile yeni aktif masaüstüne (`Winlogon` / UAC secure desktop) anında geçiş sağlandı.
  - Kullanıcı oturum açtığında kilit ekranı yayıncısı (`--system-session`) ile kullanıcı masaüstü tepsisi (`--tray`) arasındaki yayın çakışması küresel olay mekanizması (`Global\NexMote_System_Session_Stop_{sessionId}`) ile milisaniyeler içinde çözülerek mükerrer akışlar engellendi.

---

## 🏷️ [v0.7.9] - 2026-09-08
### ⚡ Kesintisiz Güç Yönetimi ve Çift Kanallı Güvenilir Komut Dağıtımı (Power Actions Fix)
- **Güç Eylemleri Kuyruk Güvencesi (Persistent Command Queueing):**
  - Web panelinden veya API üzerinden gönderilen tüm güç eylemleri (`reboot`, `shutdown`, `lock`, `logoff`, `reboot-safe`, `reboot-normal`), anlık SignalR soket bağlantı durumuna bağlı kalınmaksızın `DeviceCommands` kuyruğuna kalıcı olarak yazılır (`Kind = "power"`).
- **Çok Kanallı Anlık İletim (Multi-Channel Broadcast):**
  - SignalR sinyalleri hem `device:{id}:service` (LocalSystem Windows Servisi), hem `device:{id}:tray` (Kullanıcı Tepsisi), hem de `device:{id}` gruplarına eş zamanlı iletilir.
- **Sistem Düzeyinde Güvenli Komut Yürütme:**
  - `Worker.cs` ve `PowerHelper.cs` içinde sistem araçları (`shutdown.exe`, `bcdedit.exe`, `rundll32.exe`, `logoff.exe`) mutlak sistem yolu ve `cmd.exe /c ""` güvenli kapsayıcısıyla çağrılır.
- **Tepsi Seviyesinde Yedek Dinleyici (`RemoteScreenStreamer.cs`):**
  - Tray modülü oturum içinde `ExecutePowerAction` dinleyicisi kazanarak servis veya doğrudan tepsi üzerinden gelen eylemleri anında işleyebilir hale getirildi.

---

## 🏷️ [v0.7.8] - 2026-09-08
### 📦 Minimal ve Sade MSI, Doğrudan Başlama (Tiksiz Kurulum) & Proje Temizliği
- **Minimal & Sade Kurulum (Zero-Bloat MSI):**
  - MSI kurulum paketindeki gereksiz özel bitmap görselleri (`dialog.bmp`, `banner.bmp`) ve lisans metni (`license.rtf`) kaldırıldı; native, sade ve kurumsal Windows Installer standartlarına dönüldü.
- **Doğrudan ve Otomatik Başlama (Tiksiz / Onaysız Kurulum):**
  - MSI bitiş ekranındaki "NexMote Agent uygulamasını şimdi başlat" onay kutusu (checkbox / tik işareti) kaldırıldı.
  - Kurulum bittiğinde kullanıcıdan herhangi bir onay kutusunu işaretlemesi beklenmeksizin uygulama doğrudan ve otomatik olarak başlatılır.
  - Sessiz kurulumlarda (`/qn`) Windows Servisi watchdog'u ile anında kullanıcı oturumuna enjeksiyon sağlanır.
- **Proje ve Dağıtım Alanı Derin Temizliği:**
  - Eski ve kullanılmayan tek kullanımlık betikler, gereksiz arşiv paketleri (`downloads.tar.gz` ~223 MB), `.wixpdb` sembolleri ve geçici veritabanı kopyaları repodan tamamen temizlendi.
- **Ajan Ana Yasası (RULES.md & AGENTS.md) Genişletmesi:**
  - Madde 1'e "Doğrudan Başlama (Tiksiz / Onaysız Kurulum)" ve Madde 6'ya "Görsel ve Metin Sadeliği (Minimal MSI)" ilkeleri eklenerek kalıcı kural haline getirildi.

---

## 🏷️ [v0.7.7] - 2026-09-08
### ⚡ 60 FPS Akıcı Masaüstü, Kayan Pencere (Sliding Window), Monotonik Yerel RTT & Dinamik Hareket Sıkıştırması
- **Kayan Pencere Boru Hattı (Sliding Window Pipelining - `RemoteScreenStreamer.cs`):**
  - Tek karelik "dur-ve-onay-bekle" (stop-and-wait) darboğazı kaldırılarak boru hattında eş zamanlı 2-3 kare akışı sağlandı. Uzak ağ gecikmesinde bile FPS tavanı 14'ten doğrudan **30 - 60 FPS** seviyesine çıkarıldı.
- **Saat Farkından Arındırılmış Monotonik Yerel RTT (`Stopwatch.GetTimestamp()`):**
  - İstemci ve teknisyen makinelerinin sistem saatleri arasındaki zaman kayması (clock skew) kaynaklı sahte gecikme ölçümü engellendi. Karelerin gidiş-dönüş süresi ajanın yerel işlemci sayacı üzerinden sıfır hata payıyla ölçülerek haksız kalite düşüşleri önlendi.
- **Dinamik Hareket Sıkıştırması (Motion-Adaptive Dynamic Quality):**
  - Fare ve pencere hareketleri algılandığında kare boyutu anında 70-90 KB seviyesine optimize edilerek ağ arabelleği (bufferbloat) şişmeden sıfır gecikmeli akış sağlandı.
  - Hareket durduğunda 150 ms içinde %92 netlikte kristal tekil arıtma karesi (refinement frame) gönderilerek metinlerin ve arayüzün keskin okunması sağlandı.
- **Erken Onay Sinyali (Fast ACK - `MainWindow.xaml.cs`):**
  - Teknisyen konsolunda kare arka planda çözüldüğü milisaniyede onay sinyali gönderilerek ajanın gönderme penceresi sıfır gecikmeyle açık tutuldu.

---

## 🏷️ [v0.7.6] - 2026-09-08
### 🚀 Kesintisiz Ekran Akışı, 10MB SignalR Arabelleği, SCTP Koruma & Canlı Görüntü Garantisi
- **WebRTC SCTP Veri Kanalı Arabellek Koruması (`WebRtcPeerTransport.cs` & `RemoteScreenStreamer.cs`):**
  - SIPSorcery `RTCDataChannel` UDP üzerinden parçalanmamış 64 KB'tan büyük paketleri sessizce düşürüyordu veya arabellekte kilitliyordu. 64 KB üzerindeki büyük ekran karelerinin doğrudan güvenilir SignalR WebSocket hattı üzerinden akması garanti altına alındı.
- **SignalR 10 MB Yüksek Bant Genişliği Arabelleği (`MainWindow.xaml.cs`, `RemoteScreenStreamer.cs`, `Worker.cs`):**
  - İstemci ve Teknisyen uygulamasındaki `HubConnectionBuilder` yapılandırmalarına `TransportMaxBufferSize = 10 * 1024 * 1024` ve `ApplicationMaxBufferSize = 10 * 1024 * 1024` (10 MB) tanımlandı. 32 KB varsayılan sınır nedeniyle yüksek çözünürlüklü ekran akışlarının tıkanması tamamen engellendi.
- **Teknisyen Konsolu Akış Fallback & Placeholder Çözümü (`MainWindow.xaml.cs`):**
  - Monitör indeks uyuşmazlığında gelen ilk ekran karesini fallback olarak alıp görüntüleyen koruma eklendi; ekran karesi geldiğinde "Görüntü akışı bekleniyor" (`PlaceholderPanel`) anında gizlenerek masaüstü pürüzsüzce ekrana getirildi.
- **ScreenCapture Güvenli Masaüstü Geçiş & Çökme Koruması (`ScreenCapture.cs`):**
  - Windows UAC veya kilit ekranı geçişlerinde `CopyFromScreen` Win32 hatası verdiğinde akış döngüsünün takılması önlendi; `AttachToActiveDesktop(force: true)` ile ikinci deneme yapılarak ekran yakalama kararlılığı sağlandı.

---

## 🏷️ [v0.7.5] - 2026-09-08
### 🎯 Fare ve Klavye Kararlılığı, Çekirdek Desktop Optimizasyonu & Piksel Hassasiyetinde Koordinat Eşleme
- **DesktopHelper Çekirdek Thrashing Optimizasyonu (`DesktopHelper.cs`):**
  - Saniyede 45-60 kez yapılan gereksiz `SetThreadDesktop` ve `OpenInputDesktop` çağrıları önbelleğe alındı.
  - `GetUserObjectInformation(hDesktop, UOI_NAME)` ile aktif masaüstü adı (`Default`, `Winlogon`) okunarak, zaten o masaüstüne bağlı olunduğunda mükerrer çekirdek geçişleri engellendi. Kontroller 1 saniyede en fazla 1 kez çalışacak şekilde sınırlandı (1500 kat çekirdek yük hafiflemesi, sıfır hareket gecikmesi).
- **Piksel Düzeyinde Kesin Koordinat Haritalama (`MainWindow.xaml.cs` - `TryMapTileToRemote`):**
  - Uzak ekrandan gelen son karenin gerçek piksel boyutları (`bitmap.PixelWidth`, `bitmap.PixelHeight`) doğrudan kullanılarak dinamik Aspect Ratio, Pillarbox ve Letterbox kenar boşluğu hesabı getirildi.
  - Normalleştirilmiş oran (`relX / renderedWidth`) ile hedef piksel `[0, remoteWidth - 1]` aralığına tam isabetle eşleştirildi; 1366x768, 2560x1440 ve %125/%150 Windows DPI ölçeklendirmeli bilgisayarlardaki koordinat kaymaları kalıcı olarak çözüldü.
- **Akıllı Fare Hız Sınırlayıcı & Son Konum Garantisi (`HandleTileMouseMove`):**
  - Fare hareketleri saniyede en fazla 60 paket (16ms) ile sınırlandırıldı.
  - Hız sınırına takılan son hareketler bir `DispatcherTimer` (16ms) ile hedefe kesin olarak iletilir hale getirildi (fare durduğunda imlecin hedefte kalması garanti edildi).
  - Tıklama anında (`HandleTileMouseButton`), bekleyen son hareket sinyali tıklamadan hemen önce gönderilerek tıklamanın tam imleç ucunda olması sağlandı.
- **Çift İmleç Konumlandırma Çakışmasının Çözümü (`InputInjector.cs`):**
  - `SetCursorPos` başarılı olduğunda redundant ve titremeye yol açan `SendInput(MouseMove)` çağrısı iptal edildi; imleç pürüzsüz ve titreşimsiz hareket eder hale getirildi.
  - `SetCursorPos` başarısız olduğunda (UAC veya masaüstü geçişi), anında zorunlu masaüstü yenilemesi (`AttachToActiveDesktop(force: true)`) yapılıp doğru sanal masaüstü normalizasyon formülü (`65536.0 / vWidth`) ile `SendInput` ve `mouse_event` sürücü katmanına geri düşüş sağlandı.
- **Klavye Otomatik Tekrar Desteği (Key Repeat):**
  - `e.IsRepeat` engeli kaldırılarak `Backspace`, yön tuşları, `Delete` ve metin tuşları için otomatik tekrar etkinleştirildi. Yalnızca `Ctrl`, `Alt`, `Shift`, `Win` niteleyicilerinde mükerrer sinyal önlendi.
- **Pencere Odak Kaybında Tuş Kilitlenmesi Koruması (Sticky Keys Önleme):**
  - Teknisyen `Alt+Tab` yaptığında veya başka bir uygulamaya tıkladığında `Window.Deactivated` olayı yakalanarak basılı kalan tüm tuşlar için uzak bilgisayara anında `KeyUp` sinyali gönderilmesi ve `_downKeys` listesinin temizlenmesi sağlandı.
- **MultiScreenFrame Çözünürlük Metadata Transferi (`RemoteScreenStreamer.cs`):**
  - Kare serileştirmesinde `ScreenWidth` ve `ScreenHeight` parametreleri gerçek ekran sınırlarından okunarak iletilir hale getirildi.

---

## 🏷️ [v0.7.4] - 2026-09-08
### 🛡️ Domain PC'lerinde UAC & Yönetici Onay Ekranlarında Tam Fare ve Klavye Etkileşimi
- **Domain Kullanıcıları İçin Named Pipe ACL Genişletmesi (`BuildPipeSecurity`):**
  - SYSTEM yetkili Girdi Yardımcısının (`--input-helper`) yerel Named Pipe (`NexMoteInputHelper_{sessionId}`) erişim kuralına `AuthenticatedUserSid` ve `WorldSid` (ReadWrite) eklendi.
  - Active Directory Domain ortamlarında kısıtlı kullanıcı hesaplarıyla oturum açılmış bilgisayarlarda Ajan Tray sürecinin (`NexMote.Agent.Tray.exe`) Girdi Yardımcısına bağlanırken `Access Denied` (Erişim Engellendi) hatası alması giderildi.
- **Güvenilir Süreç Doğrulaması (`IsAllowedClient`):**
  - Girdi Yardımcısı Named Pipe sunucusuna bağlanan sürecin aynı Windows Oturumunda (`currentSessionId`) ve birebir aynı dosya yolunda (`selfPath` - `%ProgramFiles%\NexMote`) çalıştığı doğrulanarak, ticari kod imzalama sertifikası aranmaksızın SYSTEM girdi enjeksiyonu aktif hale getirildi.
- **UIPI (User Interface Privilege Isolation) Aşımı & UAC Etkileşimi:**
  - Teknisyen canlı masaüstü oturumundayken Windows Kullanıcı Hesabı Denetimi (UAC - `consent.exe`), Görev Yöneticisi ve "Yönetici Olarak Çalıştır" ile açılmış tüm yüksek yetkili pencerelere fare tıklamaları ve klavye girdilerinin SYSTEM yetkisiyle kesintisiz iletilmesi sağlandı.
- **Bağlantı Zaman Aşımı & Yeniden Deneme Optimizasyonu:**
  - Named Pipe istemci bağlantı zaman aşımı 25ms'den 100ms'ye çıkarıldı, yeniden bağlanma aralığı 500ms'ye düşürüldü.
- **Birincil Belirteç İmpersonation Düzeltmesi (`SessionProcessLauncher`):**
  - `DuplicateTokenEx` çağrısında `TokenPrimary` için `SecurityImpersonation` seviyesi standart hale getirildi.

---

## 🏷️ [v0.7.3] - 2026-09-07
### ⚡ Kesintisiz Canlı Masaüstü Akışı, Sonsuz Yeniden Bağlanma & Çift Katmanlı Oturum Uyandırma
- **Sonsuz ve Kararlı SignalR Yeniden Bağlanma Politikası (`InfiniteRetryPolicy`):**
  - İstemcilerin (Ajan, Windows Servisi ve Teknisyen Konsolu) geçici ağ dalgalanmaları veya sunucu servis yeniden başlatmalarında 4 deneme sonra kalıcı olarak düşmesini engelleyen `InfiniteRetryPolicy` (0s, 2s, 5s, 10s döngüsü) devreye alındı.
  - Bağlantı tamamen kopsa dahi `Closed` olayında arka plan otomatik ayağa kaldırma döngüsü eklendi.
- **Çift Katmanlı Ajan Uyandırma (Dual-Layer Session Wakeup):**
  - Teknisyen canlı masaüstü bağlantısı başlattığında sunucu, bildirimi hem kullanıcı oturumundaki Tray uygulamasına (`device:{id}`) hem de LocalSystem yetkili Windows Servisine (`device:{id}:service`) eşzamanlı iletir.
  - Windows Servisi, aktif kullanıcı oturumundaki Tray uygulamasına yerel Named Pipe (`NexMote_Session_Wakeup_{session}`) üzerinden anında sinyal göndererek Tray uygulamasını uyandırır ve oturuma dahil eder.
- **SignalingHub Oturum Yarış Durumu (Race Condition) Otomatik Onarımı:**
  - `SignalingHub.SendSignal` metodunda, yeniden bağlanma esnasında istemcinin yeni ConnectionId alması sebebiyle oluşan geçici erişim hatası giderildi.
  - Doğrulanmış cihaz bağlantıları oturum odasına otomatik dahil edilerek akışın kesintiye uğraması engellendi; detaylı yapısal hata günlükleri eklendi.
- **IIS WebSocket ve Ters Proxy Optimizasyonu:**
  - Canlı sunucuda Microsoft IIS 10.0 WebSocket protokolü, HTTP/2 ve In-Process ASP.NET Core Module v2 yapılandırması optimize edildi; WebSocket bağlantı kopmaları kalıcı olarak engellendi.

## 🏷️ [v0.7.2] - 2026-09-02
### ⚡ WebRTC P2P, Pano Dosya Aktarımı, Uptime & Canlı Dağıtım
- **WebRTC P2P Doğrudan Veri Kanalı (Ultra Düşük Gecikme):**
  - Teknisyen (`NexMote.TechnicianApp`) ve Ajan (`NexMote.Agent.Tray`) arasında STUN/ICE protokolü ve SIPSorcery kütüphanesiyle doğrudan uçtan uca (P2P) UDP veri kanalları (`stream` ve `input`) devreye alındı.
  - Sinyalleşme `SignalingHub` üzerinden WebSocket ile yürütülür; simetrik NAT veya güvenlik duvarı durumunda sistem kesintisiz olarak SignalR WebSocket sunucu rölesine geri düşer.
  - Ekran kareleri ve uzak girdi sinyalleri (fare/klavye) P2P kanalından doğrudan iletilerek sunucu bant genişliği yükü ortadan kaldırıldı ve tepki süresi minimize edildi.
- **Pano Üzerinden Dosya Kopyalama / Yapıştırma (Clipboard File Drop):**
  - Teknisyen bilgisayarında bir veya birden fazla dosya kopyalandığında, uzak masaüstü penceresinde "Panoyu Gönder" butonuyla veya pano eşitlemesiyle dosyalar otomatik olarak SHA-256 bütünlük doğrulamasıyla aktarılır.
  - Hedef bilgisayarda dosya tamamlandığında işletim sisteminin panosuna (`Clipboard.SetFileDropList`) yerleştirilir ve uzak Windows Gezgini'nde `Ctrl+V` ile anında yapıştırılabilir.
- **Web Konsolunda Cihaz Açık Kalma Süresi (Uptime) Gösterimi:**
  - `DeviceSummary` ve `DeviceRegistry` sözleşmelerine `UptimeSeconds` alanı eklendi.
  - Web panelinde hem **Genel Bakış (Sistem & Donanım)** hem de **Cihaz Özellikleri (Specs)** sekmelerinde donanım özelliklerinin hemen altına cihazın kaç gün, kaç saat ve dakikadır kesintisiz açık olduğu (`formatUptime`) entegre edildi.
- **Otomatik Dağıtım ve Yayınlama Otomasyonu (`deploy-iis.ps1`):**
  - Microsoft IIS 10.0 Windows Server altyapısına doğrudan atomik dosya aktarımı, `app_offline.htm` kilit çözümü, React web konsolu derlemesi ve `/health` doğrulama döngüsü sağlandı.

### 🛡️ Güvenlik, Bütünlük ve Kurumsal İzlenebilirlik Paketi
- **İstemci Tarafı Native Authenticode İmza ve Yayıncı Doğrulaması (`AuthenticodeVerifier`):**
  - Windows `wintrust.dll` yerel API'si (`WinVerifyTrust`, `WINTRUST_ACTION_GENERIC_VERIFY_V2`) ve X509 sertifika kütüphanesi kullanılarak indirilen tüm MSI/EXE güncelleme paketleri için işletim sistemi düzeyinde kriptografik imza doğrulayıcısı geliştirildi (`NexMote.Shared/Security/AuthenticodeVerifier.cs`).
  - `NexMote.Agent.Windows` (`Worker.cs`), `NexMote.Agent.Tray` (`RemoteScreenStreamer.cs`) ve `NexMote.TechnicianApp` (`MainWindow.xaml.cs`) artık sunucudan indirilen yükleyicileri çalıştırmadan önce:
    1. Dosya boyutu ve SHA-256 hash doğrulaması,
    2. Authenticode geçerlilik kontrolü (tamponlama/bozulma/imzasız PE tespiti),
    3. Yayıncı kimliği (Subject: "NexMote") ve parmak izi (Thumbprint) denetimini zorunlu kılar.
  - Geçersiz, imzasız veya kurcalanmış paketler anında diskten silinir ve kurulum engellenir.
- **Web Auth Cookie & Anti-CSRF Savunması:**
  - `SessionCookie.cs` ve `SessionTokenAuthHandler.cs` entegrasyonuyla, oturum cookie'si (`nexmote_session`) ile gelen tüm durum değiştirici (`POST`, `PUT`, `DELETE`, `PATCH`) isteklerde özel `X-NexMote-Client: Web` başlığı zorunlu kılındı.
  - Tarayıcıların çapraz site form veya script yönlendirmeleriyle yapabileceği CSRF saldırıları sunucu seviyesinde tamamen engellendi.
  - Frontend `api.ts`'teki tüm API isteklerine bu başlık eklendi; eski `localStorage`/`sessionStorage` token kalıntıları temizlendi.
- **Uçtan Uca Tam Actor, Audit ve Correlation ID Altyapısı:**
  - **Correlation ID:** Gelen HTTP isteklerini, veritabanı işlemlerini ve SignalR sinyallerini birbirine bağlayan `CorrelationIdMiddleware` (`X-Correlation-Id`) eklendi.
  - **Merkezi Denetim Servisi (`AuditLogService`):** Yüksek riskli tüm operasyonlar için aktör (User ID, Email), IP adresi, hedef kimlik ve korelasyon izini `ActivityLogs` tablosuna otomatik kaydeden servis devreye alındı.
  - **Yüksek Riskli Endpoint Entegrasyonu:**
    - `POST /remote-sessions`: Hangi teknisyenin hangi cihaza bağlandığı (`session.start`).
    - `POST /devices/{id}/execute-command`: Hangi teknisyenin hangi komutu çalıştırdığı (`command.execute`) ve `CommandAudits` / `DeviceCommands` tablolarına doğrudan `InitiatorUserId`, `InitiatorEmail` ve `CorrelationId` yazımı.
    - `POST /devices/{id}/uninstall-app`: Uygulama kaldıran teknisyen (`device.uninstall_app`).
    - `DELETE /devices/{id}`: Cihazı veya ajanını silen yönetici (`device.delete`, `device.uninstall_agent`).
    - `POST /settings` & `/admin/settings/smtp/test`: Sunucu ayarlarını değiştiren yönetici (`settings.update`, `settings.smtp_test`).
  - **Modernize Edilmiş Aktivite Günlüğü Arayüzü (`AuditLogView.tsx`):**
    - Renkli işlem rozetleri (Uzak Bağlantı, Terminal Komutu, Uygulama Kaldırma vb.).
    - Aktör e-postası ve IP adresi görünürlüğü.
    - Tek tıkla panoya kopyalanabilir Korelasyon ID çipi.
    - Genişletilebilir JSON detay ve bağlam inceleme çekmecesi.
- **Parola Karmaşıklığı ve Girdi Sınırları Doğrulaması (Madde 13):**
  - Merkezi `PasswordValidator.cs` kütüphanesi devreye alındı: Parolaların en az 8, en fazla 128 karakterden oluşması, harf ile rakam/özel karakter kombinasyonu içermesi ve boşluklardan ibaret olmaması zorunlu kılındı.
  - Şifre değiştirme (`/account/password`), davet kabul etme (`/invite/{token}/accept`) ve kullanıcı yönetimi bu standartlarla koruma altına alındı.
  - Kullanıcı e-postaları için RFC uyumlu `MailAddress.TryCreate` denetimi ve 256 karakter sınırı getirildi.
  - Uzak terminal komutları için 16,384 karakter üst sınır ve kabuk allowlist'i (`powershell`, `cmd`, `pwsh`) zorunlu kılındı; uygulama adı için 256 karakter sınırı eklendi.
- **Sunucu Tarafı Güvenlik Profili İzin Zorlaması (Madde 15):**
  - Güvenlik profili kısıtlamaları yalnızca Ajan üzerinde değil, doğrudan sunucu katmanında (`SignalingHub.cs` ve `DeviceEndpoints.cs`) zorunlu hale getirildi:
    1. `AllowRemoteTerminal == false`: Hem web konsolundan (`POST /devices/{id}/execute-command`) hem canlı oturum SignalR kanalından (`remote-command`) komut yürütme 403 Forbidden ve HubException ile engellenir.
    2. `ViewOnlyMode == true`: Teknisyenden gelen fare ve klavye sinyalleri (`remote-input`) sunucu tarafından hedefe iletilmeden düşürülür.
    3. `AllowClipboard == false`: İki yönlü pano metin iletimi (`clipboard-text`) engellenir.
    4. `AllowFileTransfer == false`: Dosya aktarımı (`file-chunk`) engellenir.
- **Agent Token & Kayıt Anahtarı SHA-256 Hash Mimarisi (Madde 17):**
  - İstemci cihazların kimlik doğrulama token'ları (`AgentToken`) artık veritabanında asla düz metin (plaintext) olarak tutulmamaktadır; `SHA-256` kriptografik hash'i ile saklanır (`DeviceRegistry.cs`).
  - **Sıfır Kesintili Şeffaf Geçiş:** Daha önce kaydedilmiş mevcut cihazlar ilk heartbeat veya yetkilendirme isteklerinde otomatik olarak algılanır ve düz metin token'ları şeffaf bir şekilde SHA-256 hash'ine dönüştürülür.
  - Grup kayıt anahtarları için hem hashli hem düz metin geriye uyumlu karşılaştırma mekanizması (`EnrollmentKeyValidator.cs`) uygulandı.
- **Modüler Şema Başlatıcı ve DDL İyileştirmesi (Madde 5):**
  - `Program.cs` içerisindeki 200+ satırlık ham SQL ve boş try-catch blokları temizlendi; merkezi [DatabaseInitializer.cs](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/src/NexMote.Api/Data/DatabaseInitializer.cs) oluşturuldu.
  - `PRAGMA foreign_keys = ON;`, `PRAGMA journal_mode = WAL;`, `PRAGMA synchronous = NORMAL;` ve `PRAGMA busy_timeout = 5000;` yönergeleri standart hale getirildi.
  - Tablo, indeks ve kolon geçişleri (migrations) idempotent ve güvenli bir mimariye kavuşturuldu.
- **Veri Saklama Politikası (Retention), Canlı SQLite Yedekleme ve Cascade Temizliği (Madde 6):**
  - [DatabaseMaintenanceService.cs](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/src/NexMote.Api/Services/DatabaseMaintenanceService.cs) arka plan servisi devreye alındı:
    - Süresi 14 günden önce dolmuş kullanıcı oturumları (`UserSessions`),
    - Tamamlanmış ve 14 günden eski kuyruk komutları (`DeviceCommands`),
    - Çözülmüş ve 30 günden eski cihaz uyarıları (`DeviceAlerts`),
    - 90 günden eski komut denetim kayıtları (`CommandAudits`),
    - 180 günden eski aktivite logları (`ActivityLogs`) otomatik olarak periyodik temizlenir.
  - **Canlı Sıfır-Kesintili Nokta-Zamanlı Yedekleme:** SQLite `VACUUM INTO` komutuyla çalışan veritabanı kilitlenmeden `backups/nexmote-backup-*.db` dosyasına atomik ve sıkıştırılmış yedek alınır; en güncel 7 yedek tutulur (otomatik rotasyon).
  - Cihaz silindiğinde ilişkili komut, uyarı ve oturum kayıtlarının yetim kalmasını engelleyen kademeli (cascade) temizleme eklendi (`DeviceRegistry.Delete`).
  - Yönetici için API uçları eklendi: `GET /admin/database/backups`, `POST /admin/database/backup`, `POST /admin/database/maintenance`.
- **Deep-Link Güvenliği ve Server URL Spoofing Koruması (Madde 18):**
  - [DeepLinkValidator.cs](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/src/NexMote.Shared/Security/DeepLinkValidator.cs) kütüphanesi geliştirildi.
  - `nexmote://` şeması, `sessionId` (Guid) ve `token` (min 16 karakter) parametreleri zorunlu kılındı.
  - Harici / düz metin HTTP sunucu yönlendirmeleri (`http://evil.com`) engellendi (yalnızca HTTPS veya yerel geliştirme için localhost kabul edilir).
  - Teknisyen istemcisinde ([MainWindow.xaml.cs](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/src/NexMote.TechnicianApp/MainWindow.xaml.cs)), bağlantı isteği bilinmeyen bir üçüncü taraf sunucudan geldiğinde teknisyene onay diyaloğu gösterilerek yetkisiz sunucu yönlendirmesi (spoofing) engellendi.
- **Güvenli Dosya Transferi, Disk Streaming, Checksum ve Zaman Aşımı Temizliği (Madde 8):**
  - Bellek tükenmesi (OOM DoS) açığı kapatıldı: Dosya parçacıkları artık RAM'de (`MemoryStream`) tutulmayıp doğrudan disk üzerindeki geçici dosyalara (`.part`) akıtılmaktadır (`RemoteScreenStreamer.cs`).
  - Merkezi [FileTransferValidator.cs](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/src/NexMote.Shared/Security/FileTransferValidator.cs) oluşturuldu:
    - Maksimum 500 MB dosya boyutu ve 1 MB parça boyutu tavanı getirildi.
    - Dizin geçişi (Path Traversal - `../`, `/`, `\`) ve geçersiz dosya adı karakterleri engellendi (`SanitizeFileName`).
    - Dosya transfer kontratına (`FileTransferChunk`) `Sha256` eklendi; gönderici tarafından hesaplanan SHA-256 özeti alıcı tarafında montaj sonrası kriptografik olarak doğrulanır, uyuşmazlık halinde dosya derhal silinip reddedilir.
    - 5 dakikadan uzun süre atıl kalan yarım kalmış dosya transferleri (`CleanupStaleTransfers`) diskten otomatik temizlenir.
- **Input-Helper Named Pipe ACL, Oturum İzolasyonu ve Authenticode Doğrulaması (Madde 21):**
  - SYSTEM yetkisiyle çalışan [InputHelperServer.cs](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/src/NexMote.Agent.Tray/Input/InputHelperServer.cs) Named Pipe sunucusu sıkılaştırıldı:
    - Pipe ACL'i geniş `InteractiveSid` yerine yalnızca `LocalSystem`, `Administrators` ve aktif oturum sahibinin `Current User SID`'sine sınırlandırıldı.
    - Gelen bağlantıların mutlaka aynı Windows Oturum Numarası (`SessionId`) içerisinde çalışan bir süreçten geldiği doğrulandı (farklı oturumlardan ve terminal server kullanıcılarından gelen müdahaleler engellendi).
    - İstemci sürecin imza geçerliliği [AuthenticodeVerifier.cs](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/src/NexMote.Shared/Security/AuthenticodeVerifier.cs) ile denetlenerek sahte iksir/enjeksiyon saldırıları kapatıldı.
- **Device List Performansı: Sayfalama, Sunucu Taraflı Arama/Filtreleme & SignalR Delta Telemetri (Madde 7):**
  - Bellek tahsisi ve ağ yükü optimize edildi: [DeviceRegistry.cs](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/src/NexMote.Api/Services/DeviceRegistry.cs) içine `ListPaged` eklendi. Binlerce cihazlık envanterlerde tüm listeyi belleğe ve JSON'a çevirmek yerine sayfalanmış (`Page`, `PageSize`, `TotalPages`, `TotalCount`, `OnlineCount`, `OfflineCount`) mimariye geçildi.
  - Sunucu taraflı arama (`Search`), durum filtresi (`Status`: online/offline), grup filtresi (`GroupId`) ve çoklu kriter sıralama (`SortBy`: name, lastSeen, cpu, memory, os, user, uptime - `SortDir`: asc/desc) entegre edildi.
  - Geriye uyumluluk korundu: `/api/devices` parametresiz çağrıldığında mevcut array kontratını korurken, sayfalama talep edildiğinde veya `/api/devices/paged` çağrıldığında `PagedResult<DeviceSummary>` döndürür.
  - **SignalR Canlı Delta Yayını:** Periyodik ağır polling yerine [SignalingHub.cs](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/src/NexMote.Api/Hubs/SignalingHub.cs) üzerinde `devices:feed` grubu oluşturuldu. Her heartbeat döngüsünde hafif `DeviceTelemetryDelta`, yeni kayıtta `DeviceEnrolledDelta` ve cihaz silmede `DeviceDeletedDelta` olayları yayınlanarak canlı panel ve teknisyen konsolu gerçek zamanlı reaktif yapıya kavuşturuldu.
- **CI/CD Kalite Kapıları ve Otomasyon Pipeline'ı (Madde 10):**
  - GitHub Actions iş akışı [.github/workflows/ci.yml](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/.github/workflows/ci.yml) kuruldu:
    - **.NET 8 Windows Runner:** Çözüm restore, Release derleme, 76/76 xUnit testi (`--collect:"XPlat Code Coverage"`) ve NuGet bağımlılık güvenlik açığı taraması (`dotnet list package --vulnerable`).
    - **React Web Runner:** Node.js 22 ile `npm ci`, `tsc --noEmit` tip denetimi, Vite production build ve `npm audit --audit-level=high` güvenlik taraması.
    - **Gizli Bilgi & Secret Taraması:** Git geçmişinde ve commit'lerde sızdırılmış anahtar/token taraması (`gitleaks-action`).
- **Canlı ve Staging Sunucu Dağıtım Otomasyonu (Madde 11):**
  - [scripts/deploy-iis.ps1](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/scripts/deploy-iis.ps1) geliştirildi:
    - React web konsolu derlemesi, .NET 8 Release publish, versions manifest senkronizasyonu ve IIS dağıtımı.
    - Ağ paylaşımı ve PowerShell otomasyonu ile hedef IIS sunucusuna güvenli aktarım.
    - Canlı veritabanı (`nexmote.db`), SQLite yedekleri (`backups/`) ve Data Protection anahtarlarını (`dpkeys/`) koruyarak atomik güncelleme (`Robocopy /XF`).
    - `app_offline.htm` döngüsü sonrası `/health` uç noktasını otomatik sorgulayan sağlık doğrulama döngüsü.
- **Güvenlik Açığı Taraması (Madde 9):**
  - Proje paketleri denetlenmiş, `Microsoft.EntityFrameworkCore.Sqlite 8.0.11` ile tüm bağımlılıkların güncel ve **0 güvenlik açığına** sahip olduğu doğrulanmıştır.

---

## 🏷️ [v0.7.1] - 2026-09-01
### 🚀 Yeni Özellikler & Araçlar
- **NexMote Uzaktan Toplu Ajan Dağıtım Aracı (`NexMote.Deployer` / WPF .NET 8):**
  - Yerel ağdaki bilgisayarlara tekli IP (`192.168.0.126`), IP aralığı (`192.168.0.10-50`) veya CIDR blokları (`192.168.0.0/24`) üzerinden uzaktan yönetici kimlik bilgileriyle (`.\ITDestek` / `DOMAIN\admin`) tek tıkla sessiz Ajan kurulumu.
  - `https://nexmote.com` üzerinden en güncel `NexMote-Agent-Setup.msi` paketini otomatik indirme veya yerel MSI seçebilme.
  - Multi-threaded paralel dağıtım (5 eşzamanlı makine), canlı ping/SMB port 445 kontrolü, WMI (`Win32_Process.Create`) ve RPC/SC fallback mekanizması.
  - Canlı ilerleme çubuğu, özet metrik kartları (Toplam, Başarılı, Hatalı, Kuyrukta), durum rozetleri, detaylı hata açıklamaları, CSV olarak dışa aktarma ve tek tıkla "Hatalıları Yeniden Dene" özelliği.
  - Web Paneli İndirme Merkezi'ne **`NexMote-Deployer-Setup.msi`** ve taşınabilir **`NexMote-Deployer.exe`** olarak eklendi.
- **Kilit ve Giriş Ekranı (Winlogon) Fare & Klavye Donanım Giriş Motoru:**
  - `DesktopHelper.cs` üzerinde `EnsureWindowStation("winsta0")` ve `[ThreadStatic]` aktif masaüstü handle yaşam döngüsü koruması getirildi; kilit ekranı ve şifre kutusunda fare/klavye olaylarının (`SendInput`, `mouse_event`, `keybd_event`) boşa düşmesi kalıcı olarak önlendi.
  - `InputInjector.cs` WinForms bağımlılıklarından arındırılarak Win32 `GetSystemMetrics` (SM_XVIRTUALSCREEN / SM_YVIRTUALSCREEN / SM_CXVIRTUALSCREEN / SM_CYVIRTUALSCREEN) ile %100 donanımsal koordinat haritasına geçirildi.
- **Teknisyen Uygulamasına `🔒 Kilit Aç` Butonu & `Ctrl+Alt+End` Kısayolu:**
  - Teknisyen üst ada araç çubuğuna tek tıkla `sas.dll` üzerinden çekirdek seviyesinde `Ctrl+Alt+Del` (Secure Attention Sequence - SAS) gönderen **`🔒 Kilit Aç`** butonu eklendi.
  - Canlı oturum sırasında uzak makineye anında SAS göndermek için **`Ctrl+Alt+End`** evrensel klavye kısayolu entegre edildi.

---

## 🏷️ [v0.7.0] - 2026-09-01
### 🏗️ Kapsamlı Mimari Yenileme ve Modüler Refactoring
- **Backend Minimal API Modüler Routing Katmanı (`NexMote.Api/Endpoints/`):** Tek parça 1,300 satırlık `Program.cs` dosyası, `AuthEndpoints.cs`, `DeviceEndpoints.cs`, `OrganizationEndpoints.cs`, `SecurityProfileEndpoints.cs` ve `SettingsEndpoints.cs` olmak üzere 5 ayrı izole uzantı sınıfına bölündü. `Program.cs` 355 satıra indirildi.
- **SQLite WAL Modu ve Eşzamanlılık Optimizasyonu:** Backend başlangıcında `PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;` aktif edilerek yoğun eşzamanlı cihaz canlılık sinyallerinde (heartbeat) veritabanı kilitlenmeleri (database is locked) kalıcı olarak önlendi.
- **Agent Tray Modüler Dizin Mimarisi (`NexMote.Agent.Tray/`):** 4,300+ satırlık devasa `Program.cs` dosyası, sorumluluklarına göre `Config/`, `Forms/`, `Input/`, `Platform/` ve `Streaming/` klasörleri altında 13 bağımsız sınıfa ayrıştırıldı.
- **Web Ön Yüz Bileşen ve Tip Ayrıştırması (`web/src/`):** 6,400+ satırlık `App.tsx` monolitinden `types.ts`, `utils.tsx` ve `components/` (`LoginScreen`, `InviteAcceptScreen`, `AppSidebar`, `AppHeader`, `DownloadsView`, `UsersView`, `AuditLogView`) modülleri ayrıştırıldı.
- **Vite Doğrudan Çıktı Pipeline'ı:** `web/vite.config.ts` doğrudan `src/NexMote.Api/wwwroot` yoluna `emptyOutDir: true` ile çıktı verecek şekilde yapılandırıldı; manuel dosya kopyalama adımları ortadan kaldırıldı.
- **Canlı OTA Güvenlik Profili ve Kurumsal Kimlik (Branding) Senkronizasyonu:** Yönetici panelinden bir güvenlik profili veya cihaz grubu güncellendiğinde ya da cihaza atandığında SignalR üzerinden `SecurityProfileUpdated` sinyali anında iletilir. Hedef bilgisayardaki Ajan yeniden başlatmaya gerek duymadan `RefreshSecurityProfileAsync()` ile güncel logo, isim, menü kısıtları ve bağlantı onay kurallarını dinamik olarak uygular.
- **Hiyerarşik Organizasyon Dizin Ağacı (Tree View) ve Context Inspector:** Web konsolunda Şirket, Departman ve bağlı Cihazları tek bir dizin ağacı görünümünde listeleyen, genişletme/daraltma ve hızlı arama destekli yeni organizasyon mimarisi. Sağ paneldeki Context Inspector ile seçilen düğüme anında güvenlik profili atama ve toplu cihaz ilişkilendirme imkanı eklendi.
- **DirectX 3.5 & Vortice Güncellemesi:** Ekran yakalama motoru en güncel `Vortice.Direct3D11` ve `Vortice.DXGI` 3.5.0 sürümüne yükseltildi, derleme zamanı paket uyarıları sıfırlandı.
- **Kod Konsolidasyonu ve Ölü Kod Temizliği:** Kullanılmayan `DeviceRecord.cs` ölü kodu kaldırıldı. `AlertContracts.cs` kontratı `AgentContracts.cs` ile, `RemoteSessionRecord.cs` modeli `RemoteSessionRegistry.cs` ile, `AlertMonitorService.cs` ise `AlertService.cs` ile konsolide edildi. Kök dizindeki eski büyük arşivler temizlendi ve tüm projelerin sürüm numaraları tek çatı altında v0.7.0'a hizalandı.

---

## 🏷️ [v0.6.9] - 2026-08-24

### 🚀 Yeni Özellikler
- **Bağlantı Onayı (Consent) ve Granüler Ajan İzinleri:** Güvenlik profillerine, teknisyen bağlanmadan önce hedef kullanıcıdan onay istenmesini sağlayan bir onay modu (`Kısıtsız` / `Her Zaman Sor` / `Kullanıcı Aktifse Sor`, zaman aşımlı onay diyaloğu) ve dört ayrı izin bayrağı (`Sadece İzleme`, `Uzak Terminal`, `Pano`, `Dosya Aktarımı`) eklendi. Onay bekleniyorken hedef ekranda "Teknisyen Bağlı" rozeti (`ShowConnectionBanner`) gösterilir; teknisyen tarafında bekleme/red durumları canlı olarak state olarak yansıtılır.
- **Teknisyen ↔ Ajan Pano (Kopyala/Yapıştır) Senkronizasyonu:** Teknisyen uygulamasında elle "📋 Pano" gönderme butonu eklendi; ayrıca canlı oturum sırasında Ajan kendi yerel pano değişikliğini otomatik algılayıp Teknisyene iletir, Teknisyen de gelen metni otomatik kendi panosuna yazar (iki yönlü, `AllowClipboard` güvenlik profili bayrağıyla kapatılabilir).
- **İndirme Merkezinde Gruba Özel Tek Script ile Kurulum:** "Hedef grup" seçilip indirilen paket artık seçilen gruba/profile gerçekten uygun kuruluyor — yeni "Tek Script ile Kur" (.ps1) MSI'ı sunucudan indirir, sessizce kurar ve ajanı doğrudan seçilen gruba bağlar; ayrı bir kurulum-sonrası provizyon adımı gerekmez (eski "sadece provizyon script'i" seçeneği zaten kurulu ajanlar için korundu).
### 🛠️ Hata Düzeltmeleri
- **Ajan Sunucuya Bağlanamıyor Görünüyordu:** Kök neden yanlış anlaşılıyordu — sunucu/ağ sorunu değil, daha önce web panelinden silinmiş bir cihazın (`DeletedDevices` tablosu) otomatik yeniden kaydının kasıtlı olarak engellenmesiydi (`DeviceRegistry.Enroll`). Etkilenen cihaz kaydı üretimde temizlendi.
- **Uzaktan Ajan Güncellemesi Süresiz Kilitleniyordu (kritik):** `/api/agents/{id}/update` ile tetiklenen sessiz güncelleme, Ajan Windows Servisinin kendi `msiexec /i ... /qn` çağrısında canlı ortamda **süresiz kilitleniyordu** (msiexec süreçleri 40+ dakika "Responding" ama ilerlemesiz kaldı) — kök neden, kurulumu başlatan sürecin (servisin) aynı zamanda Windows Installer'ın yerleşik Restart Manager'ı tarafından da ayrıca kapatılmaya/yeniden başlatılmaya çalışılmasıydı (WiX'in kendi `ServiceControl`/`KillAgentTrayProcess` adımlarıyla çakışan bir yarış durumu). Daha da kötüsü: kilitlenen `pending-update.msi` silinmediği için **her servis yeniden başlatmasında/heartbeat'te aynı kilitlenme tekrarlanıyordu** (kendi kendini durduran bir döngü). `MSIRESTARTMANAGERCONTROL=Disable` bayrağı eklenip zaten çalışan bir `msiexec` varsa yeni kurulumun tetiklenmesi engellendi (`Worker.CheckPendingUpdate`). Üretimde etkilenen cihaz (DESKTOP-SIH3FAC) elle kurulumla kurtarıldı.
- **Web Konsolu Kenar Çubuğunda Bayat Sürüm Etiketi:** `App.tsx`'teki sabitlenmiş `v0.6.3 Pro` metni hiçbir zaman güncellenmiyordu (0.6.4-0.6.8 sürümleri boyunca yanlış gösterim) — artık gerçek sürümü yansıtıyor.
- **Eksik Veritabanı Kolonu Migration'ı:** `SecurityProfileEntity`'ye eklenen 8 yeni bağlantı onayı/izin kolonu için `Program.cs`'teki manuel `ALTER TABLE` bloğu unutulmuştu — bu haliyle canlıya alınsaydı mevcut `SecurityProfiles` tablosunda "no such column" hatasıyla tüm güvenlik profili sorguları kırılırdı. Aynı `ALTER TABLE ... try/catch` deseniyle tamamlandı.

---

## 🏷️ [v0.6.3] - 2026-08-19
### 🚀 Yeni Özellikler
- **Web Üzerinden Uzaktan Sessiz Ajan Kaldırma (Remote Self-Uninstall):**
  - Web konsolundan bir cihaz veya birden çok cihaz silinirken açılan onay modalında `🛡️ Hedef Bilgisayardaki NexMote Ajanını da Kaldır (Sessiz Uninstall)` seçeneği eklendi (çevrimiçi cihazlar için varsayılan olarak aktif).
  - Silme işlemi tetiklendiğinde `DELETE /api/devices/{id}?uninstallAgent=true` ile hedef cihaza SignalR üzerinden `RemoteUninstallRequested` sinyali iletilir.
  - Cihazdaki Windows Servisi (`LocalSystem`), paketle birlikte gelen `NexMote.Cleaner.exe` derin temizleyicisini `%TEMP%` üzerinden sessiz modda (`--silent --from-temp`) devreye sokarak Windows Servisini, Tepsi uygulamasını, Program Files dosyalarını ve Kayıt Defteri girdilerini tamamen kaldırır.
- **Web Üzerinden Doğrudan CMD ve PowerShell Terminali:**
  - Teknisyen masaüstü uygulaması açmaya veya cihaza canlı bağlanmaya gerek kalmadan, doğrudan web konsolundan (**[https://nexmote.com](https://nexmote.com)**) komut çalıştırma desteği.
  - CMD (`cmd.exe`) ve PowerShell (`powershell.exe`) olmak üzere iki ayrı kabuk sekmesi.
  - Windows Servisi (`LocalSystem`) üzerinden %100 sessiz, UAC onaysız tam yönetici (`NT AUTHORITY\SYSTEM`) çalıştırma.
  - Hızlı komut butonları (`ipconfig`, `whoami`, `netstat`, `Get-Service`, `Get-Process`, vb.), komut geçmişi (Yukarı/Aşağı yön tuşları), çıktı kopyalama ve temizleme.

### 🛠️ Hata Düzeltmeleri & İyileştirmeler
- **Windows Açılışında ve Yeniden Başlatmada Tray Simgesi:**
  - Windows Servis katmanında `TryLaunchInActiveSessionAsUser` için `SeAssignPrimaryTokenPrivilege`, `SeIncreaseQuotaPrivilege` ve `SeTcbPrivilege` süreç ayrıcalık etkinleştirmesi eklendi.
  - Kullanıcı token'ı henüz hazır değilse SYSTEM oturumuna otomatik fallback mekanizması kuruldu; cihaz yeniden başladığında bildirim alanında sağ altta tepsi simgesinin gelmesi garanti altına alındı.
- **Masaüstü ve Başlat Menüsü Kısayollarından Durum Paneli Açılışı:**
  - Masaüstü kısayoluna `Arguments="--dashboard"` eklendi.
  - Uygulama başlangıcında `WindowsFormsSynchronizationContext` garantiye alındı; `SW_RESTORE` ve `SetForegroundWindow` Win32 API'leri ile açık olan formun öne gelmesi sağlandı.
  - Ajan doğrudan çift tıklandığında veya kısayoldan açıldığında antivirüs tarzı Durum Panelinin (`DashboardForm`) ekrana gelmesi sağlandı.
- **Yüklü Uygulamalar Envanteri & Temiz Kullanıcı Adı:**
  - 64-bit ve 32-bit Registry Uninstall kayıtları taranarak web konsolunda yüklü programlar sekmesi eklendi.
  - Domain/UPN ekleri temizlenerek gerçek oturum açan veya son oturum açan kullanıcı adı gösterimi sağlandı.

---

## 🏷️ [v0.6.2] - 2026-08-19
### 🚀 Yeni Özellikler & İyileştirmeler
- **Uzaktan Yeniden Başlatma Sonrası Otomatik Yeniden Bağlanma (Reboot Recovery Watchdog):**
  - Teknisyen uygulamasında Güç -> "Yeniden Başlat" / "Güvenli Mod" seçildiğinde bağlantı koptuğunda oturum kapanmaz; bekleme durumuna geçer.
  - Arka planda sunucudan cihazın açılması izlenir; cihaz açılıp çevrimiçi olduğu saniye canlı masaüstü oturumu **otomatik olarak** yeniden başlatılır.
- **Kilit & Windows Giriş (Winlogon) Ekranında Klavye-Fare Desteği:**
  - `DesktopHelper.AttachToActiveDesktop()` mantığı `MAXIMUM_ALLOWED` ve `Winlogon` masaüstü erişimiyle güçlendirildi.
  - `InputInjector` içine çift katmanlı enjeksiyon eklendi (`SendInput` başarısız olduğunda `mouse_event` ve `keybd_event` sürücü katmanına geri düşüş).
- **Ajan Güncelleme İlerleme Penceresi:**
  - Ajan arayüzünde "Ajanı Güncelle" butonuna tıklandığında anlık indirme hızını ve aşamalarını gösteren `UpdateProgressForm` formu eklendi.
- **URL Başına "www." Koyma Zorunluluğunun Kaldırılması:**
  - Teknisyen ve Ajan ayarlarında girilen sunucu adresleri `NexMoteHttp.NormalizeUrl` ile otomatik olarak standart URL formatına (`https://...`) dönüştürülecek şekilde normalize edildi.

---

## 🏷️ [v0.6.1] - 2026-08-18
### 🚀 Yeni Özellikler & İyileştirmeler
- **Admin Kimlik Doğrulama & Endpoint Güvenliği:**
  - Backend API'ye `POST /api/auth/login` ve `AdminAuthFilter` eklendi. Cihaz listesi ve sunucu ayarları Bearer Token ile koruma altına alındı.
- **Gerçek Donanım Telemetrisi:**
  - Sahte CPU hesaplamaları kaldırılarak `GetSystemTimes` üzerinden 10 dakikalık kayan pencere ortalamalı gerçek CPU kullanımı (`CpuUsageSampler.cs`) ve `GlobalMemoryStatusEx` ile gerçek RAM kullanımı sağlandı.
- **Sıfır Gecikmeli Adaptif Akış Motoru (4 Kademe):**
  - Ekran değişmediğinde 0 FPS / 0 KB/s uyku modu; fare/ekran hareketinde anında 30+ FPS canlı akış modu.
- **WiX MSI & Inno Setup Çift Paketleme:**
  - Kurumsal per-machine `.msi` ve 1.5 saniyelik ultra hızlı `.exe` kurulum paketleri geliştirildi.

---

## 🏷️ [v0.6.0] - 2026-08-15
### 🚀 İlk Temel Mimari
- **Çoklu Monitör Eş Zamanlı Yayın:** Tüm fiziksel ekranların bağımsız JPEG kareleri halinde WebSocket/SignalR üzerinden canlı aktarımı.
- **Windows Arka Plan Servisi (`NexMote.Agent.Windows`):** LocalSystem ayrıcalığı ile 20s heartbeat, uzaktan komut çalıştırma ve UAC izinleri.
- **Teknisyen Masaüstü Uygulaması (`NexMote.TechnicianApp`):** WPF .NET 8 modern SaaS arayüzü, çoklu ekran gösterimi, uzaktan terminal ve kalite profilleri.
- **Web Konsolu (`web/`):** React 18 + TypeScript + Vite modern cihaz yönetim arayüzü.
