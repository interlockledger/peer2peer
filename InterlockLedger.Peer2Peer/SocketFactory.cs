/******************************************************************************************************************************

Copyright (c) 2018-2019 InterlockLedger Network
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of the copyright holder nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

******************************************************************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace InterlockLedger.Peer2Peer
{
    public sealed class SocketFactory : IDisposable
    {
        public SocketFactory(ILoggerFactory loggerFactory, ushort portDelta, ushort howManyPortsToTry = 5) {
            _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger<SocketFactory>();
            PortDelta = portDelta;
            HowManyPortsToTry = howManyPortsToTry;
        }

        public ushort HowManyPortsToTry { get; }
        public ushort PortDelta { get; }

        public static IEnumerable<IPAddress> GetAddresses(string name)
            => IPAddress.TryParse(name, out var address)
                ? (new IPAddress[] { address })
                : Dns.GetHostEntry(name).AddressList.Where(ip => IsIPV4(ip.AddressFamily));

        public void Dispose() { }

        public Socket GetSocket(string name, ushort portNumber) {
            var localaddrs = GetAddresses(name);
            return ScanForSocket(localaddrs, portNumber) ?? ScanForSocket(localaddrs, 0);
        }

        private readonly ILogger _logger;

        private static bool IsIPV4(AddressFamily family) => family == AddressFamily.InterNetwork;

        private Socket BindSocket(IPAddress localaddr, ushort port) {
            if (localaddr is null) {
                _logger.LogError($"-- No address provided while trying to bind a socket to listen at :{port}");
                return null;
            }
            try {
                var listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                listenSocket.Bind(new IPEndPoint(localaddr, port));
                listenSocket.Listen(120);
                return listenSocket;
            } catch (ArgumentOutOfRangeException aore) {
                _logger.LogError(aore, $"-- Bad port number while trying to bind a socket to listen at {localaddr}:{port}");
                return null;
            } catch (SocketException e) {
                _logger.LogError(e, $"-- Error while trying to bind a socket to listen at {localaddr}:{port}");
                return null;
            }
        }

        private Socket ScanForSocket(IEnumerable<IPAddress> localaddrs, ushort port) {
            for (ushort tries = HowManyPortsToTry; tries > 0; tries--) {
                foreach (var localaddr in localaddrs) {
                    var socket = BindSocket(localaddr, port);
                    if (socket != null)
                        return socket;
                }
                port -= PortDelta;
            }
            return null;
        }
    }
}