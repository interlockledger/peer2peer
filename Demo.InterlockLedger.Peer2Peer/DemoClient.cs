/******************************************************************************************************************************

Copyright (c) 2018-2020 InterlockLedger Network
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
using System.Text;
using System.Threading.Tasks;
using InterlockLedger.Peer2Peer;

namespace Demo.InterlockLedger.Peer2Peer
{
    internal class DemoClient : DemoBaseSink
    {
        public static readonly byte[] _livenessBytes = ToMessageBytes(AsUTF8Bytes("l"), isLast: true);

        public DemoClient() : base("Client") {
        }

        public static string Prompt => @"Command (
    x to exit,
    w to get who is answering,
    e... to echo ...,
    3... to echo ... 3 times,
    4... to echo ... 4 times from stored responder): ";

        public bool DoneReceiving { get; set; } = false;

        public override async Task<Success> SinkAsync(ReadOnlyMemory<byte> messageBytes, IActiveChannel activeChannel)
            => Received(await Process(messageBytes, activeChannel.Channel));

        protected override Func<ReadOnlyMemory<byte>> AliveMessageBuilder => BuildAliveMessage;

        protected override void Run(IPeerServices peerServices) {
            using var client = peerServices.GetClient("localhost", 8080);
            client.ConnectionStopped += (_) => _brokenConnection = true;
            var liveness = new LivenessListener(client);
            while (!_source.IsCancellationRequested && liveness.Alive && !_brokenConnection) {
                Console.WriteLine();
                Console.Write(Prompt);
                while (!Console.KeyAvailable) {
                    if (!liveness.Alive || _brokenConnection) {
                        Console.WriteLine();
                        Console.WriteLine();
                        Console.WriteLine("Lost connection with server! Abandoning...");
                        Console.WriteLine();
                        return;
                    }
                }
                var command = Console.ReadLine();
                if (command == null || command.FirstOrDefault() == 'x')
                    break;
                var channel = client.AllocateChannel(this);
                channel.SendAsync(ToMessage(AsUTF8Bytes(command), isLast: true).AllBytes).Wait();
            }
        }

        private bool _brokenConnection = false;

        private static ReadOnlyMemory<byte> BuildAliveMessage() => _livenessBytes;

        private static async Task<Success> Process(ReadOnlyMemory<byte> message, ulong channel) {
            await Task.Delay(1);
            if (!message.IsEmpty) {
                var response = Encoding.UTF8.GetString(message.ToArray().Skip(1).ToArray());
                Console.WriteLine($"[{channel}] {response}");
                return response[0] == 0 ? Success.Exit : Success.Next;
            }
            return Success.Exit;
        }

        private Success Received(Success success) {
            DoneReceiving = success == Success.Exit;
            return success;
        }

        private class LivenessListener : IChannelSink
        {
            public LivenessListener(IConnection client) => Start(client.AllocateChannel(this));

            public bool Alive { get; private set; } = true;

            public Task<Success> SinkAsync(ReadOnlyMemory<byte> messageBytes, IActiveChannel channel) => Task.FromResult(Success.Next);

            private void Start(IActiveChannel channel)
                => Task.Run(() => {
                    ReadOnlyMemory<byte> livenessOnChannel0 = new NetworkMessageSlice(0, _livenessBytes).AllBytes;
                    try {
                        while (channel.Connected) {
                            channel.SendAsync(livenessOnChannel0).Wait();
                            Task.Delay(1000).Wait();
                        }
                    } catch {
                    } finally {
                        Alive = false;
                    }
                }).RunOnThread("Liveness");
        }
    }
}