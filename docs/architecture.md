# NexMote Mimari

## Bilesenler

- Backend API: cihaz kaydi, cihaz listesi, yetki, audit ve session uretimi.
- Signaling Hub: agent ve technician app arasindaki eslestirme mesajlari.
- Web Panel: teknisyenlerin cihazlari gormesi ve baglanti baslatmasi.
- Windows Agent: cihazda LocalSystem Windows Service olarak calisir.
- User Session Helper: aktif Windows oturumunda ekran/input gorevlerini yapar.
- Technician App: teknisyen bilgisayarinda calisir ve remote session'i acan native istemcidir.

## Baglanti Akisi

1. Agent `POST /api/agents/enroll` ile kaydolur.
2. Agent `POST /api/agents/{id}/heartbeat` ile online durumunu gunceller.
3. Teknisyen web panelde `GET /api/devices` ile cihazlari listeler.
4. Teknisyen `POST /api/remote-sessions` ile oturum olusturur.
5. Web panel `nexmote://connect?sessionId=...&token=...` deep link'ini acar.
6. Technician App token ile backend'e baglanir.
7. Backend/SignalR agent'a baglanti istegi yollar.

## Windows Session Dayanikliligi

Kontrol kanali Windows Service uzerinden surer. Kullanici sign out, switch user veya lock screen yaptiginda goruntu helper'i degisebilir, ancak agent servis sunucuyla baglantiyi korur.

