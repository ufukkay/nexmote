# IIS Yayin Plani

## Sunucu Gereksinimleri

- Windows Server 2019 veya 2022
- IIS Web Server
- ASP.NET Core Hosting Bundle 8.x
- Reverse proxy/WebSocket destegi etkin
- TLS sertifikasi
- PostgreSQL sunucusu veya ileride ayrilacak database sunucusu
- DNS: `nexmote.com` veya secilecek alt alan adi

## Backend Publish

```powershell
dotnet publish backend/src/NexMote.Api/NexMote.Api.csproj -c Release -o C:\inetpub\nexmote-api
```

IIS uzerinde yeni site veya application:

- Physical path: `C:\inetpub\nexmote-api`
- App pool: No Managed Code
- Binding: HTTPS
- Environment variable: `ASPNETCORE_ENVIRONMENT=Production`
- Environment variable: `Enrollment__Key=<strong-random-key>`

## Web Panel Publish

```powershell
cd web
npm install
npm run build
```

`web/dist` icerigi IIS uzerinde statik site olarak yayinlanabilir. Alternatif olarak backend `wwwroot` altina kopyalanabilir.

## WebSocket

SignalR icin IIS'te WebSocket Protocol ozelligi acik olmali.

## Ilk Test

- `https://nexmote.com/health`
- Web panelden cihaz listesi
- Agent enrollment
- Heartbeat sonrasi cihaz online gorunumu
- `Baglan` butonu ile Technician App deep link

