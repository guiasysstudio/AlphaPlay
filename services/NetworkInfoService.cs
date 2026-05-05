using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AlphaPlay.Services
{
    public static class NetworkInfoService
    {
        public static string GetLocalIPv4Address()
        {
            try
            {
                string[] preferredPrefixes = { "192.168.", "10.", "172." };

                var candidates = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(networkInterface =>
                        networkInterface.OperationalStatus == OperationalStatus.Up &&
                        networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .SelectMany(networkInterface =>
                    {
                        IPInterfaceProperties properties = networkInterface.GetIPProperties();
                        bool hasGateway = properties.GatewayAddresses.Any(gateway =>
                            gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(gateway.Address));

                        return properties.UnicastAddresses
                            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                            .Select(address => new
                            {
                                Address = address.Address.ToString(),
                                HasGateway = hasGateway,
                                IsPrivate = IsPrivateIPv4(address.Address)
                            });
                    })
                    .Where(candidate =>
                        candidate.IsPrivate &&
                        !candidate.Address.StartsWith("127.", StringComparison.Ordinal) &&
                        !candidate.Address.StartsWith("169.254.", StringComparison.Ordinal))
                    .OrderByDescending(candidate => candidate.HasGateway)
                    .ThenBy(candidate => GetPrefixPriority(candidate.Address, preferredPrefixes))
                    .ToList();

                return candidates.FirstOrDefault()?.Address ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsPrivateIPv4(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();

            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        private static int GetPrefixPriority(string address, string[] preferredPrefixes)
        {
            for (int i = 0; i < preferredPrefixes.Length; i++)
            {
                if (address.StartsWith(preferredPrefixes[i], StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return preferredPrefixes.Length;
        }
    }
}
