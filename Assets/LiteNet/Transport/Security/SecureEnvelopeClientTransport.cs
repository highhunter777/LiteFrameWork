using System;
using System.Security.Cryptography;

namespace LiteNet.Transport
{
    /// <summary>Client-side secure envelope decorator over the narrow transport port.</summary>
    public sealed class SecureEnvelopeClientTransport : IClientTransport
    {
        private readonly IClientTransport _inner;
        private readonly SecureEnvelopeOptions _options;
        private SecureEnvelopeSession _session;
        private byte[] _clientNonce;
        private byte[] _clientHello;
        private bool _handshakeActive;
        private bool _secureConnected;
        private bool _disposed;

        public bool Connected => _secureConnected;

        public event Action OnConnected;
        public event Action<ArraySegment<byte>, bool> OnData;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        public SecureEnvelopeClientTransport(IClientTransport inner, SecureEnvelopeOptions options)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _inner.OnConnected += HandleInnerConnected;
            _inner.OnData += HandleInnerData;
            _inner.OnDisconnected += HandleInnerDisconnected;
            if (_inner is KcpTransportClient kcp) kcp.OnError += HandleInnerError;
        }

        public void Connect(string address, int port)
        {
            ThrowIfDisposed();
            ResetSession();
            _inner.Connect(address, port);
        }

        public void Disconnect()
        {
            if (_disposed) return;
            ResetSession();
            _inner.Disconnect();
        }

        public void TickIncoming() => _inner.TickIncoming();
        public void TickOutgoing() => _inner.TickOutgoing();

        public void Send(ArraySegment<byte> data, bool reliable)
        {
            if (_disposed || !_secureConnected || _session == null) return;
            byte[] packet = _session.Encrypt(data, reliable);
            _inner.Send(new ArraySegment<byte>(packet), reliable);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _inner.OnConnected -= HandleInnerConnected;
            _inner.OnData -= HandleInnerData;
            _inner.OnDisconnected -= HandleInnerDisconnected;
            if (_inner is KcpTransportClient kcp) kcp.OnError -= HandleInnerError;
            ResetSession();
            _inner.Dispose();
        }

        private void HandleInnerConnected()
        {
            if (_disposed) return;
            ResetSession();
            _handshakeActive = true;
            _clientNonce = CreateRandomNonce();
            _clientHello = SecureEnvelopeProtocol.CreateClientHello(_clientNonce);
            _inner.Send(new ArraySegment<byte>(_clientHello), true);
        }

        private void HandleInnerData(ArraySegment<byte> data, bool reliable)
        {
            if (_disposed) return;
            if (_secureConnected)
            {
                if (_session.TryDecrypt(data, out bool decryptedReliable, out byte[] plaintext))
                    OnData?.Invoke(new ArraySegment<byte>(plaintext), decryptedReliable);
                else
                    AbortSession();
                return;
            }

            if (!_handshakeActive || !reliable ||
                !SecureEnvelopeProtocol.TryParseServerHello(data, out byte[] echoedClientNonce,
                    out byte[] serverNonce, out byte[] serverProof))
            {
                AbortSession();
                return;
            }

            if (!SecureEnvelopeCrypto.FixedEquals(_clientNonce, echoedClientNonce))
            {
                AbortSession();
                return;
            }

            byte[] preSharedKey = _options.CopyPreSharedKey();
            byte[] expectedProof = SecureEnvelopeCrypto.CreateServerProof(preSharedKey, _clientHello, serverNonce);
            if (!SecureEnvelopeCrypto.FixedEquals(expectedProof, serverProof))
            {
                AbortSession();
                return;
            }

            byte[] transcript = SecureEnvelopeProtocol.CreateServerHelloTranscript(_clientNonce, serverNonce);
            byte[] clientProof = SecureEnvelopeCrypto.CreateClientProof(preSharedKey, _clientHello, transcript);
            _session = SecureEnvelopeSession.CreateClient(
                SecureEnvelopeCrypto.DeriveKeys(preSharedKey, _clientNonce, serverNonce),
                _options.MaxPayloadBytes);
            _inner.Send(new ArraySegment<byte>(SecureEnvelopeProtocol.CreateClientFinish(clientProof)), true);
            _handshakeActive = false;
            _secureConnected = true;
            OnConnected?.Invoke();
        }

        private void HandleInnerDisconnected()
        {
            bool notify = _handshakeActive || _secureConnected;
            ResetSession();
            if (notify) OnDisconnected?.Invoke();
        }

        private void HandleInnerError(string error) => OnError?.Invoke(error);

        private void AbortSession()
        {
            bool notify = _handshakeActive || _secureConnected;
            ResetSession();
            _inner.Disconnect();
            if (notify) OnDisconnected?.Invoke();
        }

        private void ResetSession()
        {
            _session?.Dispose();
            _session = null;
            _clientNonce = null;
            _clientHello = null;
            _handshakeActive = false;
            _secureConnected = false;
        }

        private static byte[] CreateRandomNonce()
        {
            var nonce = new byte[SecureEnvelopeProtocol.NonceBytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
            return nonce;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SecureEnvelopeClientTransport));
        }
    }
}
