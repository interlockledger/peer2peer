/******************************************************************************************************************************
 *
 *      Copyright (c) 2017-2018 InterlockLedger Network
 *
 ******************************************************************************************************************************/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InterlockLedger.Peer2Peer
{
    public enum Success
    {
        None = 0,
        Retry = 1,
        SwitchToListen = 4,
        Exit = 128
    }

    public interface IClientSink
    {
        int DefaultListeningBufferSize { get; }
        bool WaitForever { get; }

        Task<Success> SinkAsClientAsync(IEnumerable<ReadOnlyMemory<byte>> readOnlyBytes);
    }

    public interface INodeSink
    {
        int DefaultListeningBufferSize { get; }
        string HostAtAddress { get; }
        int HostAtPortNumber { get; }
        IEnumerable<string> LocalResources { get; }
        ulong MessageTag { get; }
        string NetworkName { get; }
        string NetworkProtocolName { get; }
        string NodeId { get; }
        string PublishAtAddress { get; }
        int PublishAtPortNumber { get; }
        IEnumerable<string> SupportedNetworkProtocolFeatures { get; }

        void HostedAt(string address, int tcpPort);

        Task<Success> SinkAsNodeAsync(IEnumerable<ReadOnlyMemory<byte>> readOnlyBytes, Action<Response> respond);
        void PublishedAt(string address, int port);
    }
}