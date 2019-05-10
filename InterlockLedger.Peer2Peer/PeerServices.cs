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

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace InterlockLedger.Peer2Peer
{
#pragma warning disable S3881 // "IDisposable" should be implemented correctly

    public sealed class PeerServices : IPeerServices, IKnownNodesServices
    {
        public PeerServices(ILoggerFactory loggerFactory, IExternalAccessDiscoverer discoverer) {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _discoverer = discoverer ?? throw new ArgumentNullException(nameof(discoverer));
            _knownNodes = new ConcurrentDictionary<string, (string address, int port, ulong messageTag, int defaultListeningBufferSize, bool retain)>();
            _clients = new ConcurrentDictionary<string, IResponder>();
            _logger = LoggerNamed(nameof(PeerServices));
        }

        public IKnownNodesServices KnownNodes => this;
        public CancellationTokenSource Source => _source ?? throw new InvalidOperationException("CancellationTokenSource was not set yet!");

        public IListener CreateListenerFor(INodeSink nodeSink)
            => Do(() => new PeerListener(nodeSink, _discoverer, Source, LoggerNamed($"{nameof(PeerListener)}#{nodeSink.MessageTag}")));

        public void Dispose() {
            if (!_disposedValue) {
                _loggerFactory.Dispose();
                _discoverer.Dispose();
                _knownNodes.Clear();
                foreach (var client in _clients.Values)
                    client?.Dispose();
                _clients.Clear();
                _disposedValue = true;
            }
        }

        public IResponder GetClient(ulong messageTag, string address, int port, int defaultListeningBufferSize)
            => Do(() => {
                var id = $"{address}:{port}#{messageTag}";
                try {
                    if (_clients.TryGetValue(id, out var existingClient))
                        return existingClient;
                    PeerClient client = BuildClient(messageTag, address, port, defaultListeningBufferSize, id);
                    if (_clients.TryAdd(id, client))
                        return client;
                    client.Dispose();
                } catch (Exception e) {
                    _logger.LogError(e, "Could not build PeerClient for {0}!", id);
                }
                return null;
            });

        public IResponder GetClient(string nodeId)
            => Do(() => _clients.TryGetValue(Framed(nodeId), out var existingClient) ? existingClient : null);

        public IPeerServices WithCancellationTokenSource(CancellationTokenSource source) {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            return this;
        }

        void IKnownNodesServices.Add(string nodeId, ulong messageTag, string address, int port, int defaultListeningBufferSize, bool retain)
            => Do(() => {
                if (string.IsNullOrWhiteSpace(nodeId))
                    throw new ArgumentNullException(nameof(nodeId));
                if (string.IsNullOrWhiteSpace(address))
                    throw new ArgumentNullException(nameof(address));
                _knownNodes[nodeId] = (address, port, messageTag, defaultListeningBufferSize, retain);
            });

        void IKnownNodesServices.Add(string nodeId, IResponder responder, bool retain)
            => Do(() => {
                if (string.IsNullOrWhiteSpace(nodeId))
                    throw new ArgumentNullException(nameof(nodeId));
                _knownNodes[nodeId] = (nodeId, 0, 0, 0, retain);
                _clients[Framed(nodeId)] = responder;
            });

        void IKnownNodesServices.Forget(string nodeId) => Do(() => { _ = _knownNodes.TryRemove(nodeId, out _); });

        IResponder IKnownNodesServices.GetClient(string nodeId) => Do(() => GetResponder(nodeId));

        bool IKnownNodesServices.IsKnown(string nodeId) => (!_disposedValue) && _knownNodes.ContainsKey(nodeId);

        private readonly ConcurrentDictionary<string, IResponder> _clients;
        private readonly IExternalAccessDiscoverer _discoverer;
        private readonly ConcurrentDictionary<string, (string address, int port, ulong messageTag, int defaultListeningBufferSize, bool retain)> _knownNodes;
        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;

        private bool _disposedValue = false;
        private CancellationTokenSource _source;

        private static string Framed(string nodeId) => $"[{nodeId}]";

        private PeerClient BuildClient(ulong messageTag, string address, int port, int defaultListeningBufferSize, string id)
            => new PeerClient(id, messageTag, address, port, Source, LoggerForClient(id), defaultListeningBufferSize);

        private T Do<T>(Func<T> func) => _disposedValue ? default : func();

        private void Do(Action action) {
            if (!_disposedValue)
                action();
        }

        private IResponder GetResponder(string nodeId)
            => _knownNodes.TryGetValue(nodeId, out (string address, int port, ulong messageTag, int defaultListeningBufferSize, bool retain) n)
                ? n.port != 0 ? GetClient(n.messageTag, n.address, n.port, n.defaultListeningBufferSize) : GetClient(nodeId)
                : null;

        private ILogger LoggerForClient(string id) => LoggerNamed($"{nameof(PeerClient)}@{id}");

        private ILogger LoggerNamed(string categoryName) => Do(() => _loggerFactory.CreateLogger(categoryName));
    }
}