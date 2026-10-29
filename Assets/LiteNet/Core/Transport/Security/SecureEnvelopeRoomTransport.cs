using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace LiteNet.Transport
{
    /// <summary>Server-side secure envelope decorator over the narrow transport port.</summary>
    public sealed class SecureEnvelopeRoomTransport : IRoomTransport
    {
        private sealed class ConnectionState : IDisposable
        {
            internal byte[] ClientHello;
            internal byte[] ClientNonce;
            internal byte[] ServerNonce;
            internal byte[] ExpectedClientProof;
            internal SecureEnvelopeSession Session;

            internal bool Established => Session != null;

            public void Dispose()
            {
                Session?.Dispose();
                Session = null;
                ClientHello = null;
                ClientNonce = null;
                ServerNonce = null;
                ExpectedClientProof = null;
            }
        }

        private readonly IRoomTransport _inner;
        private readonly SecureEnvelopeOptions _options;
        private readonly Dictionary<int, ConnectionState> _connections = new Dictionary<int, ConnectionState>();
        private readonly object _sync = new object();
        private bool _disposed;

        public event Action<int, ArraySegment<byte>, bool> OnData;
        public event Action<int> OnConnected;
        public event Action<int> OnDisconnected;

        public SecureEnvelopeRoomTransport(IRoomTransport inner, SecureEnvelopeOptions options)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _inner.OnConnected += HandleInnerConnected;
            _inner.OnData += HandleInnerData;
            _inner.OnDisconnected += HandleInnerDisconnected;
        }

        public void Start(int port)
        {
            ThrowIfDisposed();
            _inner.Start(port);
        }

        /// <summary>转发内层实际绑定端口（装饰器不改变绑定语义；<c>Start(0)</c> 的分配结果原样透出）。</summary>
        public int BoundPort => _inner.BoundPort;

        public void TickIncoming() => _inner.TickIncoming();
        public void TickOutgoing() => _inner.TickOutgoing();

        public void SendTo(int connectionId, ArraySegment<byte> data, bool reliable)
        {
            ConnectionState state = GetState(connectionId);
            if (state?.Session == null) return;
            byte[] packet = state.Session.Encrypt(data, reliable);
            _inner.SendTo(connectionId, new ArraySegment<byte>(packet), reliable);
        }

        public void Broadcast(ArraySegment<byte> data, bool reliable)
        {
            int[] connectedIds;
            lock (_sync)
            {
                var ids = new List<int>();
                foreach (KeyValuePair<int, ConnectionState> pair in _connections)
                {
                    if (pair.Value.Established) ids.Add(pair.Key);
                }
                connectedIds = ids.ToArray();
            }

            for (int i = 0; i < connectedIds.Length; i++) SendTo(connectedIds[i], data, reliable);
        }

        public void Disconnect(int connectionId) => _inner.Disconnect(connectionId);

        public string GetRemoteAddress(int connectionId) => _inner.GetRemoteAddress(connectionId);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _inner.OnConnected -= HandleInnerConnected;
            _inner.OnData -= HandleInnerData;
            _inner.OnDisconnected -= HandleInnerDisconnected;
            lock (_sync)
            {
                foreach (ConnectionState state in _connections.Values) state.Dispose();
                _connections.Clear();
            }
            _inner.Dispose();
        }

        private void HandleInnerConnected(int connectionId)
        {
            if (_disposed) return;
            lock (_sync)
            {
                if (_connections.TryGetValue(connectionId, out ConnectionState previous)) previous.Dispose();
                _connections[connectionId] = new ConnectionState();
            }
        }

        private void HandleInnerData(int connectionId, ArraySegment<byte> data, bool reliable)
        {
            if (_disposed) return;
            ConnectionState state = GetState(connectionId);
            if (state == null)
            {
                Abort(connectionId, false);
                return;
            }

            if (state.Session != null)
            {
                if (state.Session.TryDecrypt(data, out bool decryptedReliable, out byte[] plaintext))
                    OnData?.Invoke(connectionId, new ArraySegment<byte>(plaintext), decryptedReliable);
                else
                    Abort(connectionId, true);
                return;
            }

            if (state.ExpectedClientProof != null)
            {
                if (!reliable || !SecureEnvelopeProtocol.TryParseClientFinish(data, out byte[] clientProof) ||
                    !SecureEnvelopeCrypto.FixedEquals(state.ExpectedClientProof, clientProof))
                {
                    Abort(connectionId, false);
                    return;
                }

                state.Session = SecureEnvelopeSession.CreateServer(
                    SecureEnvelopeCrypto.DeriveKeys(_options.CopyPreSharedKey(), state.ClientNonce, state.ServerNonce),
                    _options.MaxPayloadBytes);
                state.ExpectedClientProof = null;
                OnConnected?.Invoke(connectionId);
                return;
            }

            if (!reliable || !SecureEnvelopeProtocol.TryParseClientHello(data, out byte[] clientNonce))
            {
                Abort(connectionId, false);
                return;
            }

            byte[] serverNonce = CreateRandomNonce();
            byte[] clientHello = Copy(data);
            byte[] preSharedKey = _options.CopyPreSharedKey();
            state.ClientHello = clientHello;
            state.ClientNonce = clientNonce;
            state.ServerNonce = serverNonce;
            state.ExpectedClientProof = SecureEnvelopeCrypto.CreateClientProof(
                preSharedKey, clientHello,
                SecureEnvelopeProtocol.CreateServerHelloTranscript(clientNonce, serverNonce));

            byte[] serverProof = SecureEnvelopeCrypto.CreateServerProof(preSharedKey, clientHello, serverNonce);
            _inner.SendTo(connectionId,
                new ArraySegment<byte>(SecureEnvelopeProtocol.CreateServerHello(clientNonce, serverNonce, serverProof)), true);
        }

        private void HandleInnerDisconnected(int connectionId)
        {
            ConnectionState state = null;
            lock (_sync)
            {
                if (_connections.TryGetValue(connectionId, out state)) _connections.Remove(connectionId);
            }

            bool established = state?.Established == true;
            state?.Dispose();
            if (established) OnDisconnected?.Invoke(connectionId);
        }

        private void Abort(int connectionId, bool established)
        {
            _inner.Disconnect(connectionId);
        }

        private ConnectionState GetState(int connectionId)
        {
            lock (_sync) return _connections.TryGetValue(connectionId, out ConnectionState state) ? state : null;
        }

        private static byte[] Copy(ArraySegment<byte> data)
        {
            var copy = new byte[data.Count];
            Buffer.BlockCopy(data.Array, data.Offset, copy, 0, data.Count);
            return copy;
        }

        private static byte[] CreateRandomNonce()
        {
            var nonce = new byte[SecureEnvelopeProtocol.NonceBytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
            return nonce;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SecureEnvelopeRoomTransport));
        }
    }
}
