// ******************************************************************************************************************************
//  
// Copyright (c) 2018-2021 InterlockLedger Network
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met
//
// * Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
// * Neither the name of the copyright holder nor the names of its
//   contributors may be used to endorse or promote products derived from
//   this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES, LOSS OF USE, DATA, OR PROFITS, OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// ******************************************************************************************************************************

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace InterlockLedger.Peer2Peer
{
    public abstract class ConnectionBase : ListenerBase, IConnection
    {
        public const string ExceptionCantProxyNoSocketMessage = "Can't proxy a connection without an active underliying socket";
        public const string ExceptionCantProxyWithSinkMessage = "Can't proxy a connection already with a default sink";
        public const string ExceptionChannelNotFoundFormat = "Channel {0} not found!!!";

        public event Action<INetworkIdentity> ConnectionStopped;

        public bool Connected => Pipeline.Connected;
        public bool KeepingAlive => _livenessKeeper is not null;
        public long LastChannelUsed => _lastChannelUsed;
        public int NumberOfActiveChannels => _channelSinks.Count;

        public IActiveChannel AllocateChannel(IChannelSink channelSink) {
            var channel = (ulong)Interlocked.Increment(ref _lastChannelUsed);
            return _channelSinks[channel] = new ActiveChannel(channel, channelSink, this);
        }

        public IActiveChannel GetChannel(ulong channel)
            => _channelSinks.TryGetValue(channel, out var activeChannel)
                ? activeChannel
                : throw new ArgumentOutOfRangeException(nameof(channel), string.Format(ExceptionChannelNotFoundFormat, channel));

        public virtual void OnPipelineStopped() {
            _logger.LogTrace($"Stopping pipeline on client {Id}");
            ConnectionStopped?.Invoke(this);
            Dispose();
        }

        public void SetDefaultSink(IChannelSink sink) {
            _sink = sink.Required(nameof(sink));
            StopAllChannelSinks();
        }

        public void SetupLivenessKeeping(Func<ReadOnlySequence<byte>> buildAliveMessage) {
            if (buildAliveMessage is not null) {
                if (_livenessKeeper is null)
                    _livenessKeeper = new LivenessKeeper(buildAliveMessage, InactivityTimeoutInMinutes, AllocateChannel);
            }
        }

        public override void Stop() => _pipeline?.Stop();

        internal Pipeline Pipeline => GetPipelineAsync().Result;

        internal Task<bool> SendAsync(NetworkMessageSlice slice) => DoAsync(() => InnerSendAsync(slice));

        internal Task<Success> SinkAsync(NetworkMessageSlice slice) => DoAsync(() => InnerSinkAsync(slice));

        protected readonly ConcurrentDictionary<ulong, IActiveChannel> _channelSinks = new();
        protected IChannelSink _sink;
        protected ISocket _socket;

        protected ConnectionBase(string id, INetworkConfig config, CancellationTokenSource source, ILogger logger, Func<ReadOnlySequence<byte>> buildAliveMessage)
            : base(id, config, source, logger) {
            _pipeline = null;
            SetupLivenessKeeping(buildAliveMessage);
        }

        protected string NetworkAddress { get; set; }
        protected int NetworkPort { get; set; }

        protected abstract ISocket BuildSocket();

        protected override void DisposeManagedResources() {
            _livenessKeeper?.Dispose();
            base.DisposeManagedResources();
            StopAllChannelSinks();
            Stop();
            _socket?.Dispose();
        }

        protected void LogError(string message) {
            if (!(_errors.TryGetValue(message, out var dateTime) && (DateTimeOffset.Now - dateTime).Hours < _hoursOfSilencedDuplicateErrors)) {
                _logger.LogError(message);
                _errors[message] = DateTimeOffset.Now;
            }
        }

        protected void StartPipeline() => _ = Pipeline;

        private const int _hoursOfSilencedDuplicateErrors = 8;
        private static readonly Dictionary<string, DateTimeOffset> _errors = new();
        private readonly AsyncLock _pipelineLock = new();
        private long _lastChannelUsed = 0;
        private LivenessKeeper _livenessKeeper;
        private Pipeline _pipeline;

        private async Task<Pipeline> GetPipelineAsync() {
            try {
                if (_pipeline is null)
                    using (await _pipelineLock.LockAsync()) {
                        var socket = BuildSocket();
                        _pipeline = new Pipeline(socket, _source, MessageTag, ListeningBufferSize, SinkAsync, OnPipelineStopped, _logger, InactivityTimeoutInMinutes); ;
                        _pipeline.ListenAsync().RunOnThread($"Pipeline {Id} to {socket.RemoteEndPoint}", PipelineThreadDone);
                    }
                return _pipeline;
            } catch (Exception se) {
                _pipeline?.Stop();
                _pipeline = null;
                throw new PeerException($"Client {Id} could not connect into remote endpoint {NetworkAddress}:{NetworkPort}{Environment.NewLine}[{se.Message}]", se);
            }
        }

        private async Task<bool> InnerSendAsync(NetworkMessageSlice slice) {
            if (Abandon)
                return false;
            try {
                try {
                    if (!slice.IsEmpty) {
                        await Pipeline.SendAsync(slice);
                    }
                    return true;
                } catch (AggregateException ae) {
                    throw ae.Flatten().InnerExceptions.First();
                }
            } catch (PeerException pe) {
                LogError(pe.Message);
            } catch (SocketException se) {
                LogError($"Client could not communicate with address {NetworkAddress}:{NetworkPort}.{Environment.NewLine}{se.Message}");
            } catch (TaskCanceledException) {
                // just ignore
            } catch (Exception e) {
                LogError($"Unexpected exception : {e}");
            }
            return false;
        }

        private async Task<Success> InnerSinkAsync(NetworkMessageSlice slice) {
            if (_channelSinks.TryGetValue(slice.Channel, out var channelSink)) {
                var result = await channelSink.SinkAsync(slice.DataList);
                if (result == Success.Exit) {
                    _channelSinks.TryRemove(slice.Channel, out _);
                    return Success.Next;
                }
                return result;
            }
            if (_sink != null) {
                var newChannel = _channelSinks[slice.Channel] = new ActiveChannel(slice.Channel, _sink, this);
                return await newChannel.SinkAsync(slice.DataList);
            }
            return Success.Next;
        }

        private void PipelineThreadDone() => _logger.LogDebug($"Pipeline {Id} thread stopped");

        private void StopAllChannelSinks() {
            foreach (var cs in _channelSinks.Values)
                cs.Stop();
            _channelSinks.Clear();
        }
    }
}