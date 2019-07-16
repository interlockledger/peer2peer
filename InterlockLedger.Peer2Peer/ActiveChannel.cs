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
using System.Threading.Tasks;

namespace InterlockLedger.Peer2Peer
{
    internal class ActiveChannel : IActiveChannel
    {
        public bool Active => !PeerConnection.Abandon;
        public ulong Channel { get; }
        public IConnection Connection => PeerConnection;
        public string Id => $"{PeerConnection.Id}@{Channel}";
        public bool Connected => PeerConnection.Connected;

        public bool Send(IEnumerable<byte> message)
            => Active && IsValid(message) ? PeerConnection.Send(new NetworkMessageSlice(Channel, message)) : true;

        public async Task<Success> SinkAsync(IEnumerable<byte> message) => await Sink.SinkAsync(message, this);

        public override string ToString() => Id;

        internal ActiveChannel(ulong channel, IChannelSink sink, ConnectionBase peerConnection) {
            Channel = channel;
            PeerConnection = peerConnection ?? throw new ArgumentNullException(nameof(peerConnection));
            Sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        internal ConnectionBase PeerConnection { get; }
        internal IChannelSink Sink { get; }

        private static bool IsValid(IEnumerable<byte> message) => message != null && message.Any();
    }
}