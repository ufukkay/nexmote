# Lokal Gelistirme

## Backend

```powershell
dotnet run --project backend/src/NexMote.Api/NexMote.Api.csproj --urls http://localhost:5080
```

## Web

```powershell
cd web
npm install
npm run dev
```

Web panel: `http://localhost:5173`

## Agent

Gelistirme sirasinda konsol uygulamasi gibi calistirilabilir:

```powershell
dotnet run --project agent-windows/src/NexMote.Agent.Windows/NexMote.Agent.Windows.csproj
```

Windows Service kurulumu ileriki installer fazinda MSI ile yapilacak.

## Technician App

```powershell
dotnet run --project technician-app/src/NexMote.TechnicianApp/NexMote.TechnicianApp.csproj -- "nexmote://connect?sessionId=00000000-0000-0000-0000-000000000000&token=dev"
```

## Deep Link Kaydi

`technician-app/protocol-registration/nexmote.reg` dosyasi Technician App kurulumunda registry'ye yazilacak. Bu islem MSI installer tarafina alinacak.

## Test Paketleri

Agent ve Technician App icin ZIP test paketlerini olusturmak:

```powershell
.\scripts\package-windows.ps1 -ServerUrl http://127.0.0.1:5080
```

Ayni agdaki baska cihazlarda test edecekseniz `127.0.0.1` yerine NexMote sunucusunun LAN IP adresini yazin:

```powershell
.\scripts\package-windows.ps1 -ServerUrl http://SUNUCU-IP:5080
```

Paketler `downloads/` klasorune yazilir ve web panelde `Indirilenler` bolumunden indirilebilir.

## MSI Paketleri

Agent ve Technician App icin MSI paketlerini olusturmak:

```powershell
.\scripts\package-msi.ps1 -ServerUrl http://SUNUCU-IP:5080
```

MSI paketleri `downloads/` klasorune yazilir:

- `nexmote-agent-win-x64.msi`
- `nexmote-technician-win-x64.msi`

Agent MSI Windows Service kurdugu icin yonetici yetkisi ister. Technician MSI `nexmote://` protokolunu makine genelinde kaydettigi icin bu ilk test surumunde o da yonetici yetkisi ister.
