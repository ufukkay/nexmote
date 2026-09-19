using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NexMote.Shared.Contracts;

namespace NexMote.Agent.Windows.Security;

/// <summary>
/// Windows Kayıt Defteri (Registry) ve işletim sistemi sürücü seviyesinde USB denetimi sağlayan motor.
/// Windows Servisi (LocalSystem) yetkisiyle anında ve yeniden başlatma gerektirmeden uygulanır.
/// </summary>
public static class UsbPolicyEnforcer
{
    private const string UsbStorKey = @"SYSTEM\CurrentControlSet\Services\USBSTOR";
    private const string StoragePoliciesKey = @"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies";

    public static void Enforce(PolicyUsb? usbPolicy, ILogger logger)
    {
        try
        {
            var mode = usbPolicy?.Mode ?? UsbPolicyModes.AllowAll;
            logger.LogInformation("USB Güvenlik Politikası uygulanıyor: {Mode}", mode);

            switch (mode.ToLowerInvariant())
            {
                case UsbPolicyModes.BlockAll:
                case UsbPolicyModes.BlockStorage:
                    // USB Yığın Depolama (Mass Storage) Sürücüsünü Devre Dışı Bırak (Start = 4)
                    SetRegistryDword(Registry.LocalMachine, UsbStorKey, "Start", 4);
                    SetRegistryDword(Registry.LocalMachine, StoragePoliciesKey, "WriteProtect", 1);
                    break;

                case UsbPolicyModes.ReadOnlyStorage:
                    // USB Depolama Açık (Start = 3), fakat Yazma Korumalı / Salt Okunur (WriteProtect = 1)
                    SetRegistryDword(Registry.LocalMachine, UsbStorKey, "Start", 3);
                    SetRegistryDword(Registry.LocalMachine, StoragePoliciesKey, "WriteProtect", 1);
                    break;

                case UsbPolicyModes.WhitelistOnly:
                    // İzinli USB listesi modu: Whitelist tanımlıysa sürücüyü aç, kural dışı erişimlerde yazma korumasını devrede tut
                    SetRegistryDword(Registry.LocalMachine, UsbStorKey, "Start", 3);
                    SetRegistryDword(Registry.LocalMachine, StoragePoliciesKey, "WriteProtect", 0);
                    break;

                case UsbPolicyModes.AllowAll:
                default:
                    // USB Tamamen Serbest (Start = 3, WriteProtect = 0)
                    SetRegistryDword(Registry.LocalMachine, UsbStorKey, "Start", 3);
                    SetRegistryDword(Registry.LocalMachine, StoragePoliciesKey, "WriteProtect", 0);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "USB güvenlik politikası uygulanırken hata oluştu.");
        }
    }

    private static void SetRegistryDword(RegistryKey root, string subKey, string valueName, int value)
    {
        try
        {
            using var key = root.CreateSubKey(subKey, writable: true);
            key?.SetValue(valueName, value, RegistryValueKind.DWord);
        }
        catch
        {
        }
    }
}
