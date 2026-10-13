using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RemoteCli {
/// The address this computer has in its Tailscale network. The program's relay listens on it alone: a phone reaches
/// it through Tailscale's encrypted tunnel, and nothing on the Wi-Fi or the internet reaches it at all.
public static class Tailnet {
    // REMOTECLI_ADDRESS names another address to listen on instead: a test names 127.0.0.1.
    static readonly string Given = (Environment.GetEnvironmentVariable("REMOTECLI_ADDRESS") ?? "").Trim();

    /// Tailscale gives every device an IPv4 address in 100.64.0.0/10.
    public static bool Within(IPAddress ip) {
        if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] bytes = ip.GetAddressBytes();
        return bytes[0] == 100 && (bytes[1] & 0xC0) == 64;
    }

    /// This computer's address in its tailnet now, or nothing while Tailscale is not connected.
    public static string Address() {
        if (Given.Length > 0) return Given;
        try {
            return Pick(NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(a => new KeyValuePair<string, IPAddress>(n.Name + " " + n.Description, a.Address))));
        } catch (NetworkInformationException) { return ""; }
    }

    /// Of the addresses of the network adapters that are up, each named by its adapter, the one Tailscale's adapter
    /// has. A carrier may hand out addresses of the same range on another adapter; that network is not the tailnet.
    public static string Pick(IEnumerable<KeyValuePair<string, IPAddress>> addresses) {
        foreach (var address in addresses)
            if (address.Key.IndexOf("Tailscale", StringComparison.OrdinalIgnoreCase) >= 0 && Within(address.Value)) return address.Value.ToString();
        return "";
    }
}
}
