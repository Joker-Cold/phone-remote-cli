using System;
using System.Collections.Generic;
using System.Net;
using RemoteCli;

/// Which address the program listens on: the one of Tailscale's adapter, never another adapter's, even in the same range.
public static class TailnetCheck {
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static KeyValuePair<string, IPAddress> On(string adapter, string ip) { return new KeyValuePair<string, IPAddress>(adapter, IPAddress.Parse(ip)); }

    public static int Main() {
        Require(Tailnet.Within(IPAddress.Parse("100.64.0.1")) && Tailnet.Within(IPAddress.Parse("100.127.255.254")), "the ends of 100.64.0.0/10 are inside");
        Require(!Tailnet.Within(IPAddress.Parse("100.63.255.255")) && !Tailnet.Within(IPAddress.Parse("100.128.0.1")), "next to 100.64.0.0/10 is outside");
        Require(!Tailnet.Within(IPAddress.Parse("192.168.1.5")) && !Tailnet.Within(IPAddress.Parse("fd7a:115c:a1e0::1")), "a home network and IPv6 are outside");
        Require(Tailnet.Pick(new[] { On("Wi-Fi Intel(R) Wi-Fi 6", "192.168.1.5"), On("Tailscale Tailscale Tunnel", "fd7a:115c:a1e0::1"), On("Tailscale Tailscale Tunnel", "100.101.102.103") })
            == "100.101.102.103", "the IPv4 address of Tailscale's adapter is chosen");
        Require(Tailnet.Pick(new[] { On("Ethernet Realtek PCIe GbE", "100.72.1.9") }) == "", "a carrier's address in the same range on another adapter is not the tailnet");
        Require(Tailnet.Pick(new[] { On("Wi-Fi Intel(R) Wi-Fi 6", "192.168.1.5") }) == "", "without Tailscale there is no address");
        Require(Tailnet.Pick(new KeyValuePair<string, IPAddress>[0]) == "", "no adapter, no address");
        Console.WriteLine("tailnet: ok");
        return 0;
    }
}
