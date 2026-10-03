using System;

namespace LiteNet.Transport
{
    public sealed class SecureEnvelopeOptions
    {
        internal const int KeyBytes = 32;

        private readonly byte[] _preSharedKey;

        public int MaxPayloadBytes { get; }

        public SecureEnvelopeOptions(byte[] preSharedKey, int maxPayloadBytes = 64 * 1024)
        {
            if (preSharedKey == null) throw new ArgumentNullException(nameof(preSharedKey));
            if (preSharedKey.Length != KeyBytes)
                throw new ArgumentException("The secure envelope key must be exactly 32 bytes.", nameof(preSharedKey));
            if (maxPayloadBytes <= 0 || maxPayloadBytes > 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

            _preSharedKey = new byte[preSharedKey.Length];
            Buffer.BlockCopy(preSharedKey, 0, _preSharedKey, 0, preSharedKey.Length);
            MaxPayloadBytes = maxPayloadBytes;
        }

        public static SecureEnvelopeOptions FromBase64(string value, int maxPayloadBytes = 64 * 1024)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("The secure envelope key is required.", nameof(value));
            return new SecureEnvelopeOptions(Convert.FromBase64String(value), maxPayloadBytes);
        }

        public static SecureEnvelopeOptions FromEnvironment(string variableName = "LITENET_SECURE_ENVELOPE_KEY",
            int maxPayloadBytes = 64 * 1024)
        {
            if (string.IsNullOrWhiteSpace(variableName))
                throw new ArgumentException("The environment variable name is required.", nameof(variableName));
            return FromBase64(Environment.GetEnvironmentVariable(variableName), maxPayloadBytes);
        }

        internal byte[] CopyPreSharedKey()
        {
            var copy = new byte[_preSharedKey.Length];
            Buffer.BlockCopy(_preSharedKey, 0, copy, 0, _preSharedKey.Length);
            return copy;
        }
    }
}
