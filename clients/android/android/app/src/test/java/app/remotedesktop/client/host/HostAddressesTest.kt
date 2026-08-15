package app.remotedesktop.client.host

import app.remotedesktop.client.host.HostAddresses.Candidate
import app.remotedesktop.client.host.HostAddresses.Reach
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Welche Adresse dieses Handy von sich angibt.
 *
 * Die Entscheidung steht getrennt vom Einsammeln, weil das `Context` braucht und
 * diese Regel nicht — und weil hier der Fehler saß, den niemand sah: angezeigt
 * wurde verlässlich die eine Adresse, über die dieses Gerät niemand erreicht.
 */
class HostAddressesTest {

    @Test
    fun `ein VPN steht vor dem WLAN`() {
        // Arrange — Tailscale neben dem Heimnetz. Beide funktionieren; die
        // Adresse des VPN bleibt aber auch, wenn das Handy das Haus verlässt.
        val candidates = listOf(
            Candidate("192.168.178.31", Reach.LOCAL),
            Candidate("100.92.39.90", Reach.VPN),
        )

        // Act
        val ranked = HostAddresses.rank(candidates)

        // Assert
        assertEquals(listOf("100.92.39.90", "192.168.178.31"), ranked)
    }

    @Test
    fun `Mobilfunk steht hinten`() {
        // Arrange — genau der Fall, der vorher schiefging: ein WLAN ohne
        // Internet macht den Mobilfunk zum aktiven Netz, und dessen Adresse
        // stand danach ganz vorn.
        val candidates = listOf(
            Candidate("10.84.7.3", Reach.CELLULAR),
            Candidate("192.168.178.31", Reach.LOCAL),
        )

        // Act
        val ranked = HostAddresses.rank(candidates)

        // Assert
        assertEquals(listOf("192.168.178.31", "10.84.7.3"), ranked)
    }

    @Test
    fun `dieselbe Adresse steht nur einmal da`() {
        // Arrange — dasselbe Netz kommt über zwei Wege herein.
        val candidates = listOf(
            Candidate("192.168.178.31", Reach.LOCAL),
            Candidate("192.168.178.31", Reach.LOCAL),
        )

        // Act & Assert
        assertEquals(listOf("192.168.178.31"), HostAddresses.rank(candidates))
    }

    @Test
    fun `ohne Netz bleibt die Liste leer`() {
        assertEquals(emptyList<String>(), HostAddresses.rank(emptyList()))
    }

    @Test
    fun `ueber Mobilfunk kommt niemand herein`() {
        // Die Adresse liegt beim Anbieter hinter einem NAT. Sie ins Zertifikat
        // oder in den Steckbrief zu schreiben, hieße ein Versprechen abzugeben,
        // das niemand einlösen kann.
        assertFalse(HostAddresses.isRoutable(Reach.CELLULAR))

        assertTrue(HostAddresses.isRoutable(Reach.VPN))
        assertTrue(HostAddresses.isRoutable(Reach.LOCAL))
    }
}
