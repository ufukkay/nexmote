# NexMote WebRTC Gecis Plani

Bu belge, ekran akisi hattini mevcut "JPEG kare + SignalR" modelinden gercek zamanli
bir WebRTC medya hattina tasimak icin ayri bir is paketi olarak planlanmistir. Strateji
notunda ("uc uzman degerlendirmesi") bu madde bilinerek "hemen yap" degil "ayri bir is
paketi olarak planla" seklinde isaretlenmisti: mimarisi genis, native/managed medya
kodlama gerektiren ve gercek donanim + gercek agdan gecirilmeden guvenle
dogrulanamayacak bir degisiklik oldugu icin, bu oturumda kodla degil bu planla teslim
ediliyor.

## 1. Mevcut hat (referans)

- Agent tarafi: `NexMote.Agent.Tray/Program.cs` icindeki `ScreenCapture.CaptureJpegBase64`
  GDI+ `CopyFromScreen` ile ekrani yakalar, JPEG'e sikistirir, degismeyen kareleri
  hash ile atlar.
- Tasima: kare, base64 string'e cevrilip SignalR `SendSignal(sessionId, "screen-frame", base64)`
  ile teknisyene, aynen sinyalleme kanalindan (signaling relay) gonderilir.
- Teknisyen tarafi: `MainWindow.xaml.cs` `ShowFrame` base64'u `BitmapImage`'a cevirip `Image`
  kontrolune basar.
- Girdi (mouse/klavye) ayni kanaldan JSON sinyal olarak gider, agent `InputInjector`
  (Win32 `SendInput`) ile uygular.

Bu model calisiyor ve basit, ama iki temel sinirlamasi var: (1) her kare sunucu
uzerinden relay edilir (TURN/relay maliyeti + gecikme), (2) base64 kodlama ham
veriye gore ~%33 ek yuk getirir ve JPEG yazilim kodlamasi CPU'da calisir (donanim
hizlandirma yok).

## 2. Hedef mimari

```
Agent (yayinci)                    Backend (sinyalleme)              Technician App (izleyici)
------------------                 ---------------------             --------------------------
SIPSorcery RTCPeerConnection  <---  SignalingHub (SDP/ICE relay)  --->  SIPSorcery RTCPeerConnection
  + video encoder (VP8/H264)                                            + video decoder
  + capture (Media Foundation /                                         + input event channel
    GDI+ fallback)                                                        (DataChannel veya
                                                                            mevcut SendSignal)
        \                                                                    /
         \--------------------- ICE (dogrudan P2P veya TURN relay) --------/
```

- **Kutuphane secimi: SIPSorcery.** Tamamen yonetilen (managed) C#, native derleme
  gerektirmez, NuGet ile gelir, WebRTC (ICE/DTLS/SRTP/SDP) + RTP video/audio
  destegini kapsar. `Microsoft.MixedReality.WebRTC` degerlendirilip elendi: native
  derlemeler ile geliyor, bakimi durdurulmus, Windows-disi/ARM destegi zayif.
- **Sinyalleme:** Mevcut `SignalingHub` ve `session:{sessionId}` grup mekanizmasi
  aynen korunur; sadece yeni sinyal tipleri eklenir: `webrtc-offer`, `webrtc-answer`,
  `webrtc-ice-candidate`. Hub'in `SendSignal` metodu zaten generic oldugu icin hub
  kodunda degisiklik gerekmez (bu oturumda dosya transferi ve uzak komut ozellikleri
  de ayni sebeple hub'a dokunmadan eklendi).
- **Video kodlama:** Once VP8 (SIPSorcery'nin yerlesik `Vp8VpxEncoder`u ile, ek
  bagimlilik yok) ile baslanmali. H.264 donanim kodlama (Media Foundation) ikinci
  asamada, dusuk CPU kullanimi hedeflenen kurumsal dagitimlar icin eklenir.
- **NAT gecisi:** Ayni LAN/VPN icindeki kurumsal kullanim senaryosunda STUN bile
  gerekmeyebilir (dogrudan baglanti). Uzak/ev ofisi senaryosu icin coturn gibi
  acik kaynak bir TURN sunucusu self-hosted olarak backend ile birlikte
  dagitilmali (bu, "tam self-hosted" satis argumanini bozmaz cunku TURN sunucusu
  da musterinin kendi altyapisinda calisir).
- **Girdi kanali:** Mouse/klavye olaylari WebRTC `RTCDataChannel` uzerinden de
  tasinabilir (daha dusuk gecikme) ama ilk asamada mevcut SignalR `remote-input`
  yolu korunmali — video/girdi ayristirmasi riski azaltir, sadece video hattini
  degistirmis oluruz.

## 3. Asamali plan

1. **Kanit-of-kavram (2 hafta tahmini):** Tek bir gelistirici makinesinde,
   SIPSorcery ile agent -> technician tek yonlu VP8 akisini SDP/ICE'i mevcut
   SignalingHub uzerinden degistirerek calistirmak. Basari kriteri: LAN icinde
   input olmadan, sadece goruntu, JPEG hattina gore gozle gorulur dusuk gecikme.
2. **Paralel calisma (feature flag):** `RemoteScreenStreamer` icine bir
   `StreamingMode` ayari eklenip JPEG ve WebRTC hatlari bir sure yan yana
   tutulmali (ServerSettings uzerinden acilip kapanabilir). Bu, WebRTC hattinda
   sorun cikan musterilerin JPEG'e geri donebilmesini saglar — yeni bir
   ozellik icin bu tur bir geri donus yolu, canliya cikmadan once gerceklestirilmis
   uc-uca test olmadan tek hat olarak tasima riskini azaltir.
3. **Girdi ve coklu monitor entegrasyonu:** Mevcut `remote-input`,
   `select-display`, `set-quality` sinyalleri WebRTC hattinda da calismaya
   devam etmeli; `set-quality` VP8 bitrate/QP parametresine baglanmali.
4. **TURN dagitimi:** `installer/` ve `infra/` dokumanlarina self-hosted coturn
   kurulum notlari eklenmeli; backend `ServerSettings`e TURN sunucu adresi/
   kimlik bilgisi alanlari eklenmeli.
5. **Performans olcumu ve H.264'e gecis:** Gercek donanimda (dusuk/orta
   seviye is istasyonu) CPU kullanimi ve gecikme olculup, gerekiyorsa Media
   Foundation donanim H.264 kodlayiciya gecilmeli.
6. **JPEG hattinin emekliye ayrilmasi:** WebRTC hatti kurumsal musterilerde
   bir sure stabil calistiktan sonra JPEG hatti sadece "uyumluluk modu"
   olarak (ör. cok eski/kisitli agentlar icin) birakilip varsayilan olmaktan
   cikarilmali.

## 4. Riskler ve neden bu oturumda kod yazilmadi

- **Native/medya katmani test edilemez durumda:** Bu ortamda .NET SDK bile
  kurulu degil (derleme dogrulamasi yapilamiyor), gercek bir Windows ekrani,
  ag ve WebRTC gorusmesi (ICE handshake, SRTP) hic yok. JPEG hattina yapilan
  degisiklikler mekanik/dogrudan mevcut kaliplarin kopyasi oldugu icin makul
  guvenle yazilabilirdi; WebRTC ise SDP/ICE/DTLS/SRTP gibi cok sayida hareketli
  parcayi dogru sirayla baglamayi gerektirir ve tek seferde, dogrulanamadan
  yazilirsa calisan tek ozelligi (ekran/girdi akisini) kirma riski yuksektir.
- **Bagimlilik riski:** SIPSorcery NuGet paketinin surum uyumlulugu, net8.0-windows
  hedefinde WinForms/Media Foundation ile birlikte davranisi bu ortamda
  `dotnet restore`/`build` calistirilamadigi icin dogrulanamiyor.
- **Onerilen yaklasim:** Adim 1 (kanit-of-kavram) gercek bir Windows gelistirme
  makinesinde, izole bir branch'te yapilmali; mevcut JPEG hatti bozulmadan
  yanina eklenmeli (bkz. Adim 2 feature flag).

## 5. Kisa vadeli, dusuk riskli ara adim (opsiyonel, ayri bir gorev olarak)

WebRTC'ye gecmeden once, JPEG hattinin kendisinde tek basina anlamli bir
kazanim: kareyi base64 string yerine SignalR'in binary (`byte[]`) parametre
destegiyle tasimak. Bu, ~%33 kodlama yukunu kaldirir ve JSON string escaping
maliyetini ortadan kaldirir. Bilerek bu oturumda da uygulanmadi, cunku su an
tek calisan ve dogrulanabilir ozellik olan ekran akisi hattina dokunmus olurdu
ve degisikligi gormeden/test etmeden birakmak, hicbir sey yapmamaktan daha
riskli olurdu. Bir sonraki oturumda, gercek bir Windows ortaminda uctan uca
test edilerek uygulanmasi onerilir.
