using System.Security.Cryptography;
using System.Text;

namespace AnnoTape.AndroidHost;

/// <summary>Implements Xiaomi BLE Standard Auth locally, without a Mi account or network service.</summary>
internal sealed class MijiaStandardAuth : IDisposable
{
    private static readonly byte[] Ready = [0, 0, 1, 1];
    private static readonly byte[] Received = [0, 0, 1, 0];
    private static readonly byte[] RegisterSendingData = [0, 0, 0, 0, 2, 0];
    private static readonly byte[] RegisterSendingKey = [0, 0, 0, 3, 4, 0];
    private static readonly byte[] LoginSendingProof = [0, 0, 0, 0x0a, 2, 0];
    private static readonly byte[] LoginSendingKey = [0, 0, 0, 0x0b, 1, 0];
    private static readonly byte[] LoginRespondInfo = [0, 0, 0, 0x0c, 2, 0];
    private static readonly byte[] LoginRespondKey = [0, 0, 0, 0x0d, 1, 0];
    private static readonly byte[] SetupInfo = Encoding.ASCII.GetBytes("mible-setup-info");
    private static readonly byte[] LoginInfo = Encoding.ASCII.GetBytes("mible-login-info");
    private static readonly byte[] DeviceIdAad = Encoding.ASCII.GetBytes("devID");
    private static readonly byte[] DeviceIdNonce = [16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27];

    private readonly Action<string, byte[]> _write;
    private readonly Action<string> _status;
    private readonly Action<AuthKeys> _completed;
    private readonly Action _forgetStoredPairing;
    private ECDiffieHellman? _ecdh;
    private Mode _mode;
    private Stage _stage;
    private byte[]? _token;
    private byte[]? _deviceId;
    private byte[]? _myRandom;
    private byte[]? _remoteRandom;
    private byte[]? _proof;
    private byte[]? _deviceKey;
    private byte[]? _appKey;
    private byte[]? _deviceIv;
    private byte[]? _appIv;
    private MemoryStream? _parcel;
    private int _parcelCapacity;
    private int _nextFrame;

    public MijiaStandardAuth(Action<string, byte[]> write, Action<string> status, Action<AuthKeys> completed,
        Action forgetStoredPairing)
    {
        _write = write;
        _status = status;
        _completed = completed;
        _forgetStoredPairing = forgetStoredPairing;
    }

    public void Start(byte[]? token, byte[]? deviceId)
    {
        ResetTransient();
        if (token is { Length: 12 })
        {
            _mode = Mode.Login;
            _token = token.ToArray();
            _deviceId = deviceId?.ToArray();
            _myRandom = RandomNumberGenerator.GetBytes(16);
            _stage = Stage.LoginAwaitMyKeyReady;
            _status("Authenticating with the locally stored meter key…");
            WriteUpnp([0x24, 0, 0, 0]);
            WriteAvdtp(LoginSendingKey);
            return;
        }

        _mode = Mode.Register;
        _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        _stage = Stage.RegisterAwaitDeviceInfo;
        _status("Creating a private local bond. Keep the meter awake…");
        WriteUpnp([0xa2, 0, 0, 0]);
    }

    public void Handle(string characteristicUuid, byte[] value)
    {
        try
        {
            if (characteristicUuid.StartsWith("00000010-", StringComparison.OrdinalIgnoreCase))
            {
                HandleUpnp(value);
                return;
            }
            if (!characteristicUuid.StartsWith("00000019-", StringComparison.OrdinalIgnoreCase)) return;
            HandleAvdtp(value);
        }
        catch (Exception exception)
        {
            _stage = Stage.Failed;
            if (_mode == Mode.Login) _forgetStoredPairing();
            _status($"Meter authentication failed: {exception.Message}");
        }
    }

    private void HandleUpnp(byte[] value)
    {
        if (_mode == Mode.Register && Equal(value, 0x11, 0, 0, 0))
        {
            if (_token is not { Length: 12 } || _deviceId is null)
                throw new CryptographicException("The meter confirmed before key derivation completed.");
            _status("Local bond created. Starting the private session…");
            Start(_token, _deviceId);
        }
        else if (_mode == Mode.Register && Equal(value, 0x12, 0, 0, 0))
        {
            _stage = Stage.Failed;
            _status("The meter declined local binding. Wake it and try Connect meter again.");
        }
        else if (_mode == Mode.Login && Equal(value, 0x21, 0, 0, 0))
        {
            if (_token is null || _deviceKey is null || _appKey is null || _deviceIv is null || _appIv is null)
                throw new CryptographicException("The authenticated session is incomplete.");
            _stage = Stage.Authenticated;
            _completed(new(_token.ToArray(), _deviceId?.ToArray(), _deviceKey, _appKey, _deviceIv, _appIv));
        }
        else if (_mode == Mode.Login && Equal(value, 0x23, 0, 0, 0))
        {
            _stage = Stage.Failed;
            _forgetStoredPairing();
            _status("The meter replaced its local key. Disconnect and reconnect to create a new private bond.");
        }
    }

    private void HandleAvdtp(byte[] value)
    {
        if (value.Length >= 2 && value[0] == 0 && value[1] == 0)
        {
            if (value.Length == 6)
            {
                var frames = value[4] | value[5] << 8;
                _parcel?.Dispose();
                _parcelCapacity = frames * 18;
                _parcel = new MemoryStream(_parcelCapacity);
                _nextFrame = 1;
            }
            HandleControl(value);
            return;
        }

        if (_parcel is null || value.Length < 3) return;
        var frame = value[0] | value[1] << 8;
        if (frame != _nextFrame) throw new IOException($"Unexpected authentication frame {frame}; expected {_nextFrame}.");
        _nextFrame++;
        _parcel.Write(value, 2, value.Length - 2);
        if (_parcel.Length < _parcelCapacity && value.Length == 20) return;
        var message = _parcel.ToArray();
        _parcel.Dispose();
        _parcel = null;
        HandleParcel(message);
    }

    private void HandleControl(byte[] value)
    {
        if (_mode == Mode.Register) HandleRegisterControl(value);
        else HandleLoginControl(value);
    }

    private void HandleRegisterControl(byte[] value)
    {
        if (_stage == Stage.RegisterAwaitDeviceInfo && value.Length == 6
            && value[0] == 0 && value[1] == 0 && value[2] == 0 && value[3] == 0)
        {
            _stage = Stage.RegisterReceivingDeviceInfo;
            WriteAvdtp(Ready);
        }
        else if ((_stage is Stage.RegisterAwaitKeyReady or Stage.RegisterAwaitDeviceKey)
                 && value.AsSpan().SequenceEqual(Ready))
        {
            SendParcel(GetPublicKey());
            _stage = Stage.RegisterAwaitDeviceKey;
        }
        else if ((_stage is Stage.RegisterAwaitDeviceKey or Stage.RegisterAwaitKeyReady)
                 && value.AsSpan().SequenceEqual(RegisterSendingKey))
        {
            _stage = Stage.RegisterReceivingDeviceKey;
            WriteAvdtp(Ready);
        }
        else if (_stage == Stage.RegisterAwaitProofReady && value.AsSpan().SequenceEqual(Ready))
        {
            SendParcel(_proof ?? throw new CryptographicException("Registration proof was not generated."));
            _stage = Stage.RegisterAwaitProofReceipt;
        }
        else if (_stage == Stage.RegisterAwaitProofReceipt && value.AsSpan().SequenceEqual(Received))
        {
            _stage = Stage.RegisterAwaitConfirmation;
            _status("Confirming the private local bond…");
            WriteUpnp([0x13, 0, 0, 0]);
        }
    }

    private void HandleLoginControl(byte[] value)
    {
        if (_stage == Stage.LoginAwaitMyKeyReady && value.AsSpan().SequenceEqual(Ready))
        {
            SendParcel(_myRandom ?? throw new CryptographicException("Login random was not generated."));
            _stage = Stage.LoginAwaitRemoteKey;
        }
        else if (_stage == Stage.LoginAwaitRemoteKey && value.AsSpan().SequenceEqual(LoginRespondKey))
        {
            _stage = Stage.LoginReceivingRemoteKey;
            WriteAvdtp(Ready);
        }
        else if ((_stage is Stage.LoginAwaitRemoteInfo or Stage.LoginAwaitRemoteKey)
                 && value.AsSpan().SequenceEqual(LoginRespondInfo))
        {
            _stage = Stage.LoginReceivingRemoteInfo;
            WriteAvdtp(Ready);
        }
        else if (_stage == Stage.LoginAwaitProofReady && value.AsSpan().SequenceEqual(Ready))
        {
            SendParcel(_proof ?? throw new CryptographicException("Login proof was not generated."));
            _stage = Stage.LoginAwaitConfirmation;
        }
    }

    private void HandleParcel(byte[] message)
    {
        if (_stage == Stage.RegisterReceivingDeviceInfo)
        {
            _deviceId = message.Length > 4 ? message[4..] : CreateLocalDeviceId();
            WriteAvdtp(Received);
            WriteAvdtp(RegisterSendingKey);
            WriteUpnp([0x15, 0, 0, 0]);
            _stage = Stage.RegisterAwaitKeyReady;
        }
        else if (_stage == Stage.RegisterReceivingDeviceKey)
        {
            if (message.Length != 64) throw new InvalidDataException($"The meter returned a {message.Length}-byte public key.");
            DeriveRegistration(message);
            WriteAvdtp(Received);
            WriteAvdtp(RegisterSendingData);
            _stage = Stage.RegisterAwaitProofReady;
        }
        else if (_stage == Stage.LoginReceivingRemoteKey)
        {
            if (message.Length != 16) throw new InvalidDataException("The meter returned an invalid login random.");
            _remoteRandom = message;
            WriteAvdtp(Received);
            _stage = Stage.LoginAwaitRemoteInfo;
        }
        else if (_stage == Stage.LoginReceivingRemoteInfo)
        {
            DeriveLogin(message);
            WriteAvdtp(Received);
            WriteAvdtp(LoginSendingProof);
            _stage = Stage.LoginAwaitProofReady;
        }
    }

    private byte[] GetPublicKey()
    {
        var parameters = (_ecdh ?? throw new CryptographicException("Registration key is unavailable.")).ExportParameters(false);
        if (parameters.Q.X is not { Length: 32 } x || parameters.Q.Y is not { Length: 32 } y)
            throw new CryptographicException("Could not export the registration public key.");
        return [.. x, .. y];
    }

    private void DeriveRegistration(byte[] remoteKey)
    {
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = remoteKey[..32], Y = remoteKey[32..] }
        };
        using var peer = ECDiffieHellman.Create(parameters);
        var secret = (_ecdh ?? throw new CryptographicException("Registration key is unavailable."))
            .DeriveRawSecretAgreement(peer.PublicKey);
        var derived = new byte[64];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, derived, null, SetupInfo);
        CryptographicOperations.ZeroMemory(secret);
        _token = derived[..12];
        var didKey = derived[28..44];
        var deviceId = _deviceId ?? throw new CryptographicException("The meter device identifier is unavailable.");
        var ciphertext = new byte[deviceId.Length];
        var tag = new byte[4];
        using (var cipher = new AesCcm(didKey)) cipher.Encrypt(DeviceIdNonce, deviceId, ciphertext, tag, DeviceIdAad);
        _proof = [.. ciphertext, .. tag];
        CryptographicOperations.ZeroMemory(derived);
        _ecdh.Dispose();
        _ecdh = null;
    }

    private void DeriveLogin(byte[] remoteProof)
    {
        if (_token is not { Length: 12 } token || _myRandom is not { Length: 16 } mine || _remoteRandom is not { Length: 16 } remote)
            throw new CryptographicException("Login inputs are incomplete.");
        var salt = new byte[32];
        mine.CopyTo(salt, 0);
        remote.CopyTo(salt, 16);
        var derived = new byte[64];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, token, derived, salt, LoginInfo);
        _deviceKey = derived[..16];
        _appKey = derived[16..32];
        _deviceIv = derived[32..36];
        _appIv = derived[36..40];
        var remoteInput = new byte[32];
        remote.CopyTo(remoteInput, 0);
        mine.CopyTo(remoteInput, 16);
        using var deviceHmac = new HMACSHA256(_deviceKey);
        var expected = deviceHmac.ComputeHash(remoteInput);
        if (remoteProof.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(remoteProof, expected))
            throw new CryptographicException("The meter did not prove possession of the stored key.");
        using var appHmac = new HMACSHA256(_appKey);
        _proof = appHmac.ComputeHash(salt);
        CryptographicOperations.ZeroMemory(derived);
    }

    private void SendParcel(byte[] data)
    {
        var frame = 1;
        for (var offset = 0; offset < data.Length; offset += 18)
        {
            var count = Math.Min(18, data.Length - offset);
            var packet = new byte[count + 2];
            packet[0] = (byte)frame++;
            data.AsSpan(offset, count).CopyTo(packet.AsSpan(2));
            WriteAvdtp(packet);
        }
    }

    private void WriteUpnp(byte[] value) => _write("00000010-0000-1000-8000-00805f9b34fb", value);
    private void WriteAvdtp(byte[] value) => _write("00000019-0000-1000-8000-00805f9b34fb", value);
    private static bool Equal(byte[] value, params byte[] expected) => value.AsSpan().SequenceEqual(expected);

    private static byte[] CreateLocalDeviceId()
    {
        var result = new byte[20];
        var text = Encoding.ASCII.GetBytes($"blt.4.1{Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant()}00");
        text.CopyTo(result, 1);
        return result;
    }

    private void ResetTransient()
    {
        _ecdh?.Dispose();
        _ecdh = null;
        _parcel?.Dispose();
        _parcel = null;
        _proof = null;
        _deviceKey = _appKey = _deviceIv = _appIv = null;
    }

    public void Dispose()
    {
        ResetTransient();
        if (_token is not null) CryptographicOperations.ZeroMemory(_token);
        if (_myRandom is not null) CryptographicOperations.ZeroMemory(_myRandom);
        if (_remoteRandom is not null) CryptographicOperations.ZeroMemory(_remoteRandom);
    }

    internal sealed record AuthKeys(byte[] Token, byte[]? DeviceId, byte[] DeviceKey, byte[] AppKey, byte[] DeviceIv, byte[] AppIv);

    private enum Mode { Register, Login }
    private enum Stage
    {
        None, RegisterAwaitDeviceInfo, RegisterReceivingDeviceInfo, RegisterAwaitKeyReady,
        RegisterAwaitDeviceKey, RegisterReceivingDeviceKey, RegisterAwaitProofReady,
        RegisterAwaitProofReceipt, RegisterAwaitConfirmation, LoginAwaitMyKeyReady,
        LoginAwaitRemoteKey, LoginReceivingRemoteKey, LoginAwaitRemoteInfo,
        LoginReceivingRemoteInfo, LoginAwaitProofReady, LoginAwaitConfirmation,
        Authenticated, Failed
    }
}
