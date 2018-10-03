/******************************************************************************************************************************
 *
 *      Copyright (c) 2017-2018 InterlockLedger Network
 *
 ******************************************************************************************************************************/

using InterlockLedger.Peer2Peer;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace UnitTest.InterlockLedger.Peer2Peer
{
    internal class FakeDiscoverer : IExternalAccessDiscoverer
    {
        public FakeDiscoverer() {
        }

        public Task<(string address, int port, TcpListener listener)> DetermineExternalAccessAsync(INodeSink nodeSink) {
            if (nodeSink == null)
                throw new ArgumentNullException(nameof(nodeSink));
            var tcpListener = new TcpListener(new System.Net.IPEndPoint(0x80000001, nodeSink.DefaultPort));
            return Task.FromResult((address: nodeSink.DefaultAddress, port: nodeSink.DefaultPort, listener: tcpListener));
        }
    }
}