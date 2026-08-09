using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NexMote.Agent.Windows;

public static class NetworkInfo
{
    public static string? GetPrimaryIPv4Address()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
            .Select(address => address.Address.ToString())
            .FirstOrDefault();
    }
}

