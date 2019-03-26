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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace InterlockLedger.Peer2Peer
{
    public class MessageParser
    {
        public MessageParser(ulong expectedTag, Func<IEnumerable<ReadOnlyMemory<byte>>, Success> messageProcessor, ILogger logger) {
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _expectedTag = expectedTag;
            LastResult = Success.None;
        }

        public bool Continue => LastResult != Success.Exit;

        public Success LastResult { get; private set; }

        public SequencePosition Parse(ReadOnlySequence<byte> buffer) {
            long current = 0;
            void Process() {
                if (_lengthToRead == 0) {
                    _logger.LogTrace($"Message body received {_segments.Select(b => b.ToBase64())}");
                    LastResult = _messageProcessor(_segments);
                    _state = State.Init;
                } else {
                    _state = State.ReadBytes;
                }
            }
            ulong ReadBytes(ulong length) {
                if (length >= int.MaxValue)
                    throw new InvalidOperationException("Too many bytes to read!");
                long bytesToRead = Math.Min((long)length, buffer.Length - current);
                var slice = buffer.Slice(current, bytesToRead);
                foreach (var segment in slice)
                    _segments.Add(segment);
                current += bytesToRead;
                return length - (ulong)bytesToRead;
            }

            byte ReadNextByte() => buffer.Slice(current++, 1).First.Span[0];
            if (!buffer.IsEmpty)
                while (current < buffer.Length) {
                    switch (_state) {
                    case State.Init:
                        _lengthToRead = 0;
                        _tagReader.Reset();
                        _lengthReader.Reset();
                        _segments.Clear();
                        _state = State.ReadTag;
                        LastResult = Success.None;
                        break;

                    case State.ReadTag:
                        if (_tagReader.Done(ReadNextByte())) {
                            var tag = _tagReader.Value;
                            if (tag != _expectedTag)
                                throw new InvalidDataException($"Invalid message tag {tag}");
                            _logger.LogTrace("Receiving new message");
                            _state = State.ReadLength;
                        }
                        break;

                    case State.ReadLength:
                        if (_lengthReader.Done(ReadNextByte())) {
                            _lengthToRead = _lengthReader.Value;
                            _logger.LogTrace($"Expecting {_lengthReader} bytes for message body");
                            Process();
                        }
                        break;

                    case State.ReadBytes:
                        _lengthToRead = ReadBytes(_lengthToRead);
                        Process();
                        break;
                    }
                }
            return buffer.End;
        }

        private readonly ulong _expectedTag;
        private readonly ILIntReader _lengthReader = new ILIntReader();
        private readonly ILogger _logger;
        private readonly Func<IEnumerable<ReadOnlyMemory<byte>>, Success> _messageProcessor;
        private readonly List<ReadOnlyMemory<byte>> _segments = new List<ReadOnlyMemory<byte>>();
        private readonly ILIntReader _tagReader = new ILIntReader();
        private ulong _lengthToRead;
        private State _state = State.Init;

        private enum State
        {
            Init,
            ReadTag,
            ReadLength,
            ReadBytes
        }
    }
}