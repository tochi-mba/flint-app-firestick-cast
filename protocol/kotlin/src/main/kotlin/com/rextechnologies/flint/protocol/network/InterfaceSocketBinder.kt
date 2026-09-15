package com.rextechnologies.flint.protocol.network

import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket

/** Creates sockets explicitly pinned to the chosen tether-interface source address. */
class InterfaceSocketBinder(val localAddress: Inet4Address) {
    fun newServerSocket(port: Int, backlog: Int = 50): ServerSocket {
        require(port in 0..65_535)
        require(backlog > 0)
        return ServerSocket().apply {
            try {
                bind(InetSocketAddress(localAddress, port), backlog)
            } catch (failure: Throwable) {
                close()
                throw failure
            }
        }
    }

    fun newOutgoingSocket(): Socket = Socket().apply {
        try {
            bind(InetSocketAddress(localAddress, 0))
        } catch (failure: Throwable) {
            close()
            throw failure
        }
    }
}

