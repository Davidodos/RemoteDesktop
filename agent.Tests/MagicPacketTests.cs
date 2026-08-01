using RemoteDesktopAgent.Services;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Das Magic Packet lässt sich hier vollständig prüfen — es ist reine
/// Byte-Arithmetik. Ob es ankommt, hängt am Netz und steht bei den
/// Hardware-Punkten.
/// </summary>
public class MagicPacketTests
{
    [Fact]
    public void Das_Paket_ist_102_Bytes_lang()
    {
        Assert.Equal(102, MagicPacket.Build("aa:bb:cc:dd:ee:ff").Length);
    }

    [Fact]
    public void Es_beginnt_mit_sechs_mal_FF()
    {
        var packet = MagicPacket.Build("aa:bb:cc:dd:ee:ff");

        Assert.All(packet[..6], value => Assert.Equal(0xFF, value));
        Assert.NotEqual(0xFF, packet[6]);
    }

    [Fact]
    public void Danach_folgt_die_MAC_sechzehnmal()
    {
        var packet = MagicPacket.Build("aa:bb:cc:dd:ee:ff");
        byte[] mac = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];

        for (var repeat = 0; repeat < 16; repeat++)
        {
            Assert.Equal(mac, packet[(6 + repeat * 6)..(12 + repeat * 6)]);
        }
    }

    [Fact]
    public void Die_Schreibweise_der_MAC_aendert_am_Paket_nichts()
    {
        Assert.Equal(
            MagicPacket.Build("AA-BB-CC-DD-EE-FF"),
            MagicPacket.Build("aa:bb:cc:dd:ee:ff"));
    }

    [Fact]
    public void Eine_unbrauchbare_MAC_wird_abgelehnt_statt_halb_verarbeitet()
    {
        Assert.Throws<ArgumentException>(() => MagicPacket.Build("aa:bb:cc"));
    }
}
