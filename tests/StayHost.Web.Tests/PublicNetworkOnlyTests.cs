using System.Net;
using StayHost.Web.Infrastructure;

namespace StayHost.Web.Tests;

/// <summary>
/// docs/01 QL-10 — a host-typed calendar address may only reach the public
/// internet. Each address below is one a feed could have been pointed at to
/// read something from inside the deployment.
/// </summary>
public class PublicNetworkOnlyTests
{
    [Theory]
    [InlineData("127.0.0.1")]          // the app itself
    [InlineData("10.0.0.5")]
    [InlineData("172.19.0.3")]         // a docker compose network
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]    // cloud metadata
    [InlineData("100.64.0.1")]         // carrier-grade NAT
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:127.0.0.1")]   // IPv4 loopback dressed as IPv6
    [InlineData("::ffff:10.1.2.3")]
    public void Refuses_addresses_inside_a_network(string address) =>
        Assert.False(PublicNetworkOnly.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("14.225.83.93")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]         // just outside 172.16/12
    [InlineData("2606:4700:4700::1111")]
    public void Allows_public_addresses(string address) =>
        Assert.True(PublicNetworkOnly.IsPublic(IPAddress.Parse(address)));
}
