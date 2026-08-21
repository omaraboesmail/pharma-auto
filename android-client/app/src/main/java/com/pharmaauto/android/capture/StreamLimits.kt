package com.pharmaauto.android.capture

import java.io.ByteArrayOutputStream
import java.io.InputStream

fun InputStream.readAtMost(maximumBytes: Int): ByteArray {
    require(maximumBytes > 0)
    val output = ByteArrayOutputStream(minOf(maximumBytes, 64 * 1024))
    val buffer = ByteArray(64 * 1024)
    var total = 0
    while (true) {
        val read = read(buffer)
        if (read < 0) break
        total += read
        require(total <= maximumBytes) { "Document exceeds the 20 MiB page limit." }
        output.write(buffer, 0, read)
    }
    return output.toByteArray()
}
