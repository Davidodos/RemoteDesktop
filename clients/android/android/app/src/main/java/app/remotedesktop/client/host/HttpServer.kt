package app.remotedesktop.client.host

import java.io.BufferedOutputStream
import java.io.IOException
import java.io.InputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.URLDecoder
import java.util.Locale
import java.util.concurrent.Executors
import java.util.concurrent.ThreadPoolExecutor
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLServerSocketFactory

/**
 * Ein sehr kleiner HTTP/1.1-Server.
 *
 * **Warum nicht Ktor oder NanoHTTPD.** Der Host bedient eine Handvoll
 * Endpunkte für ein, zwei Clients im eigenen Netz. Ktor bringt dafür
 * Coroutinen, Netty oder CIO, slf4j und mehrere Megabyte mit — in einer App,
 * die sich über GitHub selbst aktualisiert, fällt das bei jedem Update an.
 * NanoHTTPD wäre klein genug, wird aber seit Jahren nicht mehr gepflegt. Was
 * hier steht, kann genau das, was gebraucht wird, und steht vollständig unter
 * Test.
 *
 * Blockierend, ein Thread je Verbindung: bei zwei Clients ist alles andere
 * Zierde. Die Obergrenze verhindert, dass ein Scanner im Netz das Handy mit
 * offenen Verbindungen lahmlegt.
 */
class HttpServer(
    private val port: Int,
    private val sslFactory: SSLServerSocketFactory?,
    private val handler: (Request) -> Response,
) {

    companion object {
        /** Mehr gleichzeitige Verbindungen braucht niemand, der zwei Geräte hat. */
        private const val MAX_CONNECTIONS = 16

        /** Eine stille Verbindung wird nach dieser Zeit fallen gelassen. */
        private const val READ_TIMEOUT_MS = 30_000

        /** Obergrenze für Kopfzeilen — schützt vor einem Strom ohne Ende. */
        private const val MAX_HEADER_BYTES = 16 * 1024

        /** Obergrenze für einen Rumpf, solange keine Datei hochgeladen wird. */
        private const val MAX_BODY_BYTES = 4 * 1024 * 1024

        /**
         * Die eigenen Adressen im Netz, ohne Loopback.
         *
         * Sie stehen im Zertifikat und in der Anzeige der Freigabeseite. Ein
         * Handy bekommt sie per DHCP und wechselt sie — deshalb wird bei jedem
         * Start nachgesehen und nichts gespeichert.
         */
        fun localAddresses(): List<String> =
            runCatching {
                java.net.NetworkInterface.getNetworkInterfaces().toList()
                    .filter { it.isUp && !it.isLoopback }
                    .flatMap { it.inetAddresses.toList() }
                    .filterIsInstance<InetAddress>()
                    .filter { !it.isLoopbackAddress && it.hostAddress?.contains(':') == false }
                    .mapNotNull { it.hostAddress }
                    .distinct()
            }.getOrDefault(emptyList())
    }

    /**
     * Eine Anfrage, so weit zerlegt, wie die Endpunkte sie brauchen.
     *
     * @param local Ob sie vom Gerät selbst kommt. Daran hängen die Endpunkte,
     *   die es nur dort geben darf — den Kopplungscode anzeigen und Clients
     *   widerrufen.
     */
    data class Request(
        val method: String,
        val path: String,
        val query: Map<String, String>,
        val headers: Map<String, String>,
        val body: ByteArray,
        val local: Boolean,
    ) {
        fun header(name: String): String? = headers[name.lowercase(Locale.ROOT)]

        fun text(): String = String(body, Charsets.UTF_8)
    }

    data class Response(
        val status: Int,
        val contentType: String = "application/json; charset=utf-8",
        val body: ByteArray = ByteArray(0),
        val headers: Map<String, String> = emptyMap(),
    ) {
        companion object {
            fun json(status: Int, json: String): Response =
                Response(status, body = json.toByteArray(Charsets.UTF_8))

            fun error(status: Int, message: String): Response =
                json(status, org.json.JSONObject().put("error", message).toString())
        }
    }

    @Volatile
    private var socket: ServerSocket? = null

    private var workers: ThreadPoolExecutor? = null

    val isRunning: Boolean get() = socket?.isClosed == false

    /**
     * Der Port, auf dem wirklich gelauscht wird. Bei 0 sucht das System einen
     * freien aus — im Test der einzige Weg, zwei Läufe nebeneinander laufen zu
     * lassen, ohne sich um belegte Ports zu streiten.
     */
    val boundPort: Int get() = socket?.localPort ?: port

    /** @throws IOException wenn der Port schon belegt ist. */
    fun start() {
        stop()

        val server = sslFactory?.createServerSocket(port) ?: ServerSocket(port)
        socket = server

        val pool = Executors.newFixedThreadPool(MAX_CONNECTIONS) as ThreadPoolExecutor
        workers = pool

        Thread({ accept(server, pool) }, "remotedesktop-host-$port").apply {
            isDaemon = true
            start()
        }
    }

    fun stop() {
        // Erst zumachen, dann die Arbeiter: ein Arbeiter, der noch schreibt,
        // bekommt seinen Fehler und beendet sich von allein. Andersherum liefe
        // der Annahme-Thread ins Leere.
        runCatching { socket?.close() }
        socket = null

        workers?.shutdownNow()
        workers?.awaitTermination(2, TimeUnit.SECONDS)
        workers = null
    }

    private fun accept(server: ServerSocket, pool: ThreadPoolExecutor) {
        while (!server.isClosed) {
            val client = try {
                server.accept()
            } catch (closed: IOException) {
                return
            }

            // Ist der Andrang zu groß, wird die Verbindung sofort geschlossen
            // statt in eine Warteschlange gelegt. Ein Client, der abgewiesen
            // wird, versucht es wieder; eine Warteschlange ohne Ende wäre der
            // Weg, das Handy mit einem einzigen Skript stillzulegen.
            if (pool.activeCount >= MAX_CONNECTIONS) {
                runCatching { client.close() }
                continue
            }

            runCatching { pool.execute { serve(client) } }.onFailure { client.close() }
        }
    }

    private fun serve(client: Socket) {
        client.use { connection ->
            connection.soTimeout = READ_TIMEOUT_MS

            val input = connection.getInputStream().buffered()
            val output = BufferedOutputStream(connection.getOutputStream())
            val local = connection.inetAddress?.isLoopbackAddress == true

            // Mehrere Anfragen über dieselbe Verbindung: ohne das baut der
            // Browser für jeden Aufruf einen neuen TLS-Handschlag auf, und der
            // kostet auf einem Handy spürbar mehr als die Anfrage selbst.
            while (!connection.isClosed) {
                val request = try {
                    read(input, local) ?: return
                } catch (broken: IOException) {
                    return
                }

                val response = runCatching { handler(request) }.getOrElse {
                    Response.error(500, "Im Host ist etwas schiefgegangen: ${it.message}")
                }

                try {
                    write(output, response)
                } catch (broken: IOException) {
                    return
                }

                if (request.header("connection")?.lowercase(Locale.ROOT) == "close") {
                    return
                }
            }
        }
    }

    /** @return `null`, wenn die Gegenseite aufgelegt hat. */
    private fun read(input: InputStream, local: Boolean): Request? {
        val head = readHead(input) ?: return null
        val lines = head.split("\r\n")
        val start = lines.firstOrNull()?.split(' ') ?: return null

        if (start.size < 3) {
            return null
        }

        val headers = HashMap<String, String>()

        for (index in 1 until lines.size) {
            val separator = lines[index].indexOf(':')

            if (separator > 0) {
                headers[lines[index].take(separator).trim().lowercase(Locale.ROOT)] =
                    lines[index].substring(separator + 1).trim()
            }
        }

        val target = start[1]
        val split = target.indexOf('?')
        val path = if (split < 0) target else target.take(split)
        val query = if (split < 0) emptyMap() else parseQuery(target.substring(split + 1))

        val length = headers["content-length"]?.toIntOrNull() ?: 0

        if (length > MAX_BODY_BYTES) {
            throw IOException("Rumpf mit $length Bytes ist zu groß.")
        }

        val body = ByteArray(length)
        var read = 0

        while (read < length) {
            val step = input.read(body, read, length - read)

            if (step < 0) {
                throw IOException("Der Rumpf endete zu früh.")
            }

            read += step
        }

        return Request(start[0].uppercase(Locale.ROOT), decode(path), query, headers, body, local)
    }

    /** Liest bis zur Leerzeile — mehr gehört nicht zum Kopf. */
    private fun readHead(input: InputStream): String? {
        val buffer = StringBuilder()
        var last = 0

        while (buffer.length < MAX_HEADER_BYTES) {
            val next = input.read()

            if (next < 0) {
                return if (buffer.isEmpty()) null else throw IOException("Kopf ohne Ende.")
            }

            buffer.append(next.toChar())

            if (next == '\n'.code && last == '\n'.code) {
                return buffer.toString().trimEnd('\r', '\n')
            }

            if (next != '\r'.code) {
                last = next
            }
        }

        throw IOException("Kopfzeilen sind zu lang.")
    }

    private fun write(output: BufferedOutputStream, response: Response) {
        val head = StringBuilder()
            .append("HTTP/1.1 ").append(response.status).append(' ')
            .append(reason(response.status)).append("\r\n")
            .append("Content-Type: ").append(response.contentType).append("\r\n")
            .append("Content-Length: ").append(response.body.size).append("\r\n")
            // Der Host autorisiert ausschließlich über das Token im Kopf; es
            // gibt keine Cookies. Eine fremde Seite im Browser kann damit
            // nichts erreichen, was sie nicht ohnehin dürfte — genau wie beim
            // Windows-Agent.
            .append("Access-Control-Allow-Origin: *\r\n")
            .append("Access-Control-Allow-Headers: *\r\n")
            .append("Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\n")

        response.headers.forEach { (name, value) ->
            head.append(name).append(": ").append(value).append("\r\n")
        }

        head.append("\r\n")

        output.write(head.toString().toByteArray(Charsets.US_ASCII))
        output.write(response.body)
        output.flush()
    }

    private fun parseQuery(raw: String): Map<String, String> =
        raw.split('&')
            .filter(String::isNotEmpty)
            .associate { part ->
                val separator = part.indexOf('=')

                if (separator < 0) {
                    decode(part) to ""
                } else {
                    decode(part.take(separator)) to decode(part.substring(separator + 1))
                }
            }

    private fun decode(raw: String): String =
        runCatching { URLDecoder.decode(raw, "UTF-8") }.getOrDefault(raw)

    private fun reason(status: Int): String = when (status) {
        200 -> "OK"
        400 -> "Bad Request"
        401 -> "Unauthorized"
        403 -> "Forbidden"
        404 -> "Not Found"
        405 -> "Method Not Allowed"
        500 -> "Internal Server Error"
        503 -> "Service Unavailable"
        else -> "Status"
    }
}
