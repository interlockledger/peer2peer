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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

//#pragma warning disable S3887 // Mutable, non-private fields should not be "readonly"

namespace InterlockLedger.Peer2Peer
{
    public struct NetworkMessageSlice
    {
        public NetworkMessageSlice WithChannel(ulong channel) => new NetworkMessageSlice(channel, DataList);

        public NetworkMessageSlice(ulong channel, MemoryStream ms) : this(channel, ms.ToArray()) {
        }

        public NetworkMessageSlice(ulong channel, ReadOnlyMemory<byte> readOnlyMemory) : this(channel, readOnlyMemory.ToArray()) {
        }

        public NetworkMessageSlice(ulong channel, ArraySegment<byte> data) : this(channel, new List<ArraySegment<byte>>() { data }) {
        }

        public NetworkMessageSlice(ulong channel, params byte[] array) : this(channel, array, 0, array.Length) {
        }

        public NetworkMessageSlice(ulong channel, byte[] array, int start, int length) : this(channel, new ArraySegment<byte>(array, start, length)) {
        }

        public NetworkMessageSlice(ulong channel, IEnumerable<ReadOnlyMemory<byte>> readOnlyBytes) : this(channel, readOnlyBytes.ToArraySegments()) {
        }

        public NetworkMessageSlice(ulong channel, IEnumerable<ArraySegment<byte>> dataList) {
            if (dataList == null)
                throw new ArgumentNullException(nameof(dataList));
            _segmentList = new List<ArraySegment<byte>>(dataList);
            _dataList = null;
            Channel = channel;
        }

        public byte[] AllBytes => DataList.SelectMany(m => m.ToArray()).ToArray();
        public ulong Channel { get; }
        public IList<ArraySegment<byte>> DataList => _dataList ?? (_dataList = (_segmentList ?? new List<ArraySegment<byte>>()).AsReadOnly());
        public bool IsEmpty => !DataList.Any(s => s.Count > 0);

        public NetworkMessageSlice Add(byte[] array) => Add(new ArraySegment<byte>(array));

        public NetworkMessageSlice Add(byte[] array, int start, int length) => Add(new ArraySegment<byte>(array, start, length));

        public NetworkMessageSlice Add(ArraySegment<byte> data) {
            if (_dataList == null)
                _segmentList.Add(data);
            return this;
        }

        private readonly List<ArraySegment<byte>> _segmentList;
        private ReadOnlyCollection<ArraySegment<byte>> _dataList;
    }
}