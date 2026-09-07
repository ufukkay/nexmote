using System.Net;
using System.Text.RegularExpressions;

namespace NexMote.Shared.Network;

public static class IpRangeParser
{
    public static List<string> Parse(string input)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(input)) return [];

        var tokens = input.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawToken in tokens)
        {
            var token = rawToken.Trim();
            if (string.IsNullOrWhiteSpace(token)) continue;

            // 1. Check CIDR (e.g. 192.168.1.0/24)
            if (token.Contains('/'))
            {
                var cidrIps = ParseCidr(token);
                foreach (var ip in cidrIps) result.Add(ip);
                continue;
            }

            // 2. Check Range (e.g. 192.168.1.10-192.168.1.30 or 192.168.1.10-30)
            if (token.Contains('-'))
            {
                var rangeIps = ParseRange(token);
                foreach (var ip in rangeIps) result.Add(ip);
                continue;
            }

            // 3. Single IP or Hostname
            if (IPAddress.TryParse(token, out var ipAddr))
            {
                result.Add(ipAddr.ToString());
            }
            else
            {
                // Accept valid hostname format
                if (Regex.IsMatch(token, @"^[a-zA-Z0-9\.\-_]+$"))
                {
                    result.Add(token);
                }
            }
        }

        return [.. result];
    }

    private static List<string> ParseCidr(string cidr)
    {
        var list = new List<string>();
        var parts = cidr.Split('/');
        if (parts.Length != 2) return list;
        if (!IPAddress.TryParse(parts[0], out var baseIp)) return list;
        if (!int.TryParse(parts[1], out var prefixLength) || prefixLength < 0 || prefixLength > 32) return list;

        var ipBytes = baseIp.GetAddressBytes();
        if (ipBytes.Length != 4) return list;

        uint ipNum = ((uint)ipBytes[0] << 24) | ((uint)ipBytes[1] << 16) | ((uint)ipBytes[2] << 8) | ipBytes[3];
        uint mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);
        uint startIp = ipNum & mask;
        uint endIp = startIp | ~mask;

        // Skip network and broadcast for /24 or smaller
        uint actualStart = (prefixLength >= 31) ? startIp : startIp + 1;
        uint actualEnd = (prefixLength >= 31) ? endIp : endIp - 1;

        // Cap to reasonable limit (max 1024 hosts)
        if (actualEnd - actualStart > 1024)
        {
            actualEnd = actualStart + 1024;
        }

        for (uint i = actualStart; i <= actualEnd; i++)
        {
            list.Add(UintToIp(i));
        }

        return list;
    }

    private static List<string> ParseRange(string range)
    {
        var list = new List<string>();
        var parts = range.Split(['-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return list;

        var startPart = parts[0].Trim();
        var endPart = parts[1].Trim();

        if (!IPAddress.TryParse(startPart, out var startIp)) return list;

        IPAddress endIp;
        if (byte.TryParse(endPart, out var lastByte))
        {
            // Shorthand format like 192.168.1.10-50 or 10.0.0.1-5
            var bytes = startIp.GetAddressBytes();
            bytes[3] = lastByte;
            endIp = new IPAddress(bytes);
        }
        else if (endPart.Contains('.') && IPAddress.TryParse(endPart, out var parsedEndIp))
        {
            endIp = parsedEndIp;
        }
        else
        {
            return list;
        }

        var startBytes = startIp.GetAddressBytes();
        var endBytes = endIp.GetAddressBytes();
        if (startBytes.Length != 4 || endBytes.Length != 4) return list;

        uint startNum = ((uint)startBytes[0] << 24) | ((uint)startBytes[1] << 16) | ((uint)startBytes[2] << 8) | startBytes[3];
        uint endNum = ((uint)endBytes[0] << 24) | ((uint)endBytes[1] << 16) | ((uint)endBytes[2] << 8) | endBytes[3];

        if (startNum > endNum) (startNum, endNum) = (endNum, startNum);

        // Cap to 1024 addresses
        if (endNum - startNum > 1024)
        {
            endNum = startNum + 1024;
        }

        for (uint i = startNum; i <= endNum; i++)
        {
            list.Add(UintToIp(i));
        }

        return list;
    }

    private static string UintToIp(uint ip)
    {
        return $"{(ip >> 24) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";
    }
}
