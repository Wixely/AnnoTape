using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Android;
using Android.App;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AnnoTape.App.Platform;

namespace AnnoTape.AndroidHost;

internal sealed partial class HotoLaserMeasureClient(Activity activity) : IDisposable
{
    private const int PermissionRequest = 4201;
    private const int HotoProductId = 0x09e2;
    private const string LogTag = "annotape-meter";
    private static readonly Java.Util.UUID ClientConfigurationUuid =
        Java.Util.UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;
    private readonly Activity _activity = activity;
    private readonly object _sync = new();
    private TaskCompletionSource<bool>? _permissionCompletion;
    private TaskCompletionSource<BluetoothDevice>? _scanCompletion;
    private TaskCompletionSource<bool>? _connectionCompletion;
    private BluetoothLeScanner? _scanner;
    private MeterScanCallback? _scanCallback;
    private BluetoothGatt? _gatt;
    private MeterGattCallback? _gattCallback;
    private readonly Dictionary<string, BluetoothGattCharacteristic> _characteristics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(string Uuid, byte[] Value)> _writes = new();
    private readonly object _writeSync = new();
    private MijiaStandardAuth? _auth;
    private MijiaStandardAuth.AuthKeys? _sessionKeys;
    private int _appFrameCounter;
    private bool _writeLoopRunning;
    private bool _authenticated;
    private string _deviceName = "HOTO meter";

    public event Action<string>? StatusChanged;
    public event Action<LaserMeasureReading>? MeasurementReceived;
    public bool Connected { get; private set; }
    public bool Ready => _authenticated;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await EnsurePermissionAsync(cancellationToken);
        var manager = _activity.GetSystemService(Context.BluetoothService) as BluetoothManager
            ?? throw new NotSupportedException("Bluetooth is unavailable on this device.");
        var adapter = manager.Adapter ?? throw new NotSupportedException("Bluetooth is unavailable on this device.");
        if (!adapter.IsEnabled) throw new InvalidOperationException("Turn on Bluetooth, then try again.");

        await DisconnectAsync();
        StatusChanged?.Invoke("Scanning for the HOTO meter… Turn it on so its Bluetooth icon flashes.");
        var device = await ScanAsync(adapter, cancellationToken);
        _deviceName = ReadDeviceName(device) ?? "HOTO meter";
        StatusChanged?.Invoke($"Connecting to {_deviceName}…");

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new MeterGattCallback(this);
        lock (_sync)
        {
            _connectionCompletion = completion;
            _gattCallback = callback;
            _gatt = device.ConnectGatt(_activity, false, callback, BluetoothTransports.Le)
                ?? throw new IOException("Android could not open a BLE connection.");
        }

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        catch
        {
            await DisconnectAsync();
            throw;
        }
    }

    public Task DisconnectAsync()
    {
        BluetoothGatt? gatt;
        lock (_sync)
        {
            StopScanLocked();
            gatt = _gatt;
            _gatt = null;
            _gattCallback = null;
            _connectionCompletion?.TrySetCanceled();
            _connectionCompletion = null;
            Connected = false;
            _authenticated = false;
            _auth?.Dispose();
            _auth = null;
            _sessionKeys = null;
            _appFrameCounter = 0;
        }
        if (gatt is not null)
        {
            try { gatt.Disconnect(); }
            finally
            {
                gatt.Close();
                gatt.Dispose();
            }
        }
        StatusChanged?.Invoke("Meter disconnected");
        return Task.CompletedTask;
    }

    public void HandlePermissionResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        if (requestCode != PermissionRequest) return;
        var granted = grantResults.Length > 0 && grantResults.All(result => result == Permission.Granted);
        var completion = _permissionCompletion;
        _permissionCompletion = null;
        if (granted) completion?.TrySetResult(true);
        else completion?.TrySetException(new UnauthorizedAccessException("Nearby devices permission is required to connect to the meter."));
    }

    private async Task EnsurePermissionAsync(CancellationToken cancellationToken)
    {
        var permissions = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? new[] { Manifest.Permission.BluetoothScan, Manifest.Permission.BluetoothConnect }
            : new[] { Manifest.Permission.AccessFineLocation };
        if (permissions.All(permission =>
                ContextCompat.CheckSelfPermission(_activity, permission) == Permission.Granted)) return;

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _permissionCompletion = completion;
        ActivityCompat.RequestPermissions(_activity, permissions, PermissionRequest);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        await completion.Task;
    }

    private async Task<BluetoothDevice> ScanAsync(BluetoothAdapter adapter, CancellationToken cancellationToken)
    {
        var scanner = adapter.BluetoothLeScanner
            ?? throw new InvalidOperationException("BLE scanning is unavailable while Bluetooth is off.");
        var completion = new TaskCompletionSource<BluetoothDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new MeterScanCallback(this);
        lock (_sync)
        {
            _scanner = scanner;
            _scanCompletion = completion;
            _scanCallback = callback;
        }
        var settings = new ScanSettings.Builder()
            .SetScanMode(global::Android.Bluetooth.LE.ScanMode.LowLatency)!
            .Build()!;
        scanner.StartScan(null, settings, callback);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var registration = timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token));
        try
        {
            return await completion.Task;
        }
        catch (System.OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("No HOTO meter was found. Turn it off and on, then reconnect while the Bluetooth icon flashes.");
        }
        finally
        {
            lock (_sync) StopScanLocked();
        }
    }

    private void OnScanResult(ScanResult result)
    {
        var device = result.Device;
        if (device is null) return;
        var advertisedName = result.ScanRecord?.DeviceName;
        var deviceName = ReadDeviceName(device);
        var serviceData = result.ScanRecord?.GetServiceData(
            global::Android.OS.ParcelUuid.FromString("0000fe95-0000-1000-8000-00805f9b34fb"));
        var isHotoName = IsHotoName(advertisedName) || IsHotoName(deviceName);
        var isHotoProduct = serviceData is { Length: >= 4 }
            && serviceData[2] == (byte)(HotoProductId & 0xff)
            && serviceData[3] == (byte)(HotoProductId >> 8);
        if (!isHotoName && !isHotoProduct) return;

        global::Android.Util.Log.Info(LogTag,
            $"Found meter name={advertisedName ?? deviceName ?? "(none)"} rssi={result.Rssi} " +
            $"serviceData={Hex(serviceData)}");
        _scanCompletion?.TrySetResult(device);
    }

    private static bool IsHotoName(string? value) =>
        value?.StartsWith("xiaojiang.cc_", StringComparison.OrdinalIgnoreCase) == true
        || value?.Contains("HOTO", StringComparison.OrdinalIgnoreCase) == true;

    private static string? ReadDeviceName(BluetoothDevice device)
    {
        try { return device.Name; }
        catch (Java.Lang.SecurityException) { return null; }
    }

    private void StopScanLocked()
    {
        if (_scanner is not null && _scanCallback is not null)
        {
            try { _scanner.StopScan(_scanCallback); }
            catch (Exception) { }
        }
        _scanner = null;
        _scanCallback = null;
        _scanCompletion = null;
    }

    private void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState state)
    {
        global::Android.Util.Log.Info(LogTag, $"Connection state={state} status={status}");
        if (status == GattStatus.Success && state == ProfileState.Connected)
        {
            StatusChanged?.Invoke($"Discovering {_deviceName} services…");
            if (!gatt.DiscoverServices()) FailConnection("Android could not start GATT service discovery.");
            return;
        }
        if (state == ProfileState.Disconnected)
        {
            Connected = false;
            _connectionCompletion?.TrySetException(new IOException($"The meter disconnected ({status})."));
            StatusChanged?.Invoke("Meter disconnected");
        }
    }

    private void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status, MeterGattCallback callback)
    {
        if (status != GattStatus.Success)
        {
            FailConnection($"GATT service discovery failed ({status}).");
            return;
        }

        var notifyCharacteristics = new List<BluetoothGattCharacteristic>();
        _characteristics.Clear();
        foreach (var service in gatt.Services ?? [])
        {
            global::Android.Util.Log.Info(LogTag, $"service {service.Uuid}");
            foreach (var characteristic in service.Characteristics ?? [])
            {
                global::Android.Util.Log.Info(LogTag,
                    $"characteristic {characteristic.Uuid} properties={characteristic.Properties}");
                if (characteristic.Uuid is not null)
                    _characteristics[characteristic.Uuid.ToString()] = characteristic;
                if ((characteristic.Properties & (GattProperty.Notify | GattProperty.Indicate)) != 0)
                    notifyCharacteristics.Add(characteristic);
            }
        }
        callback.BeginSubscriptions(gatt, notifyCharacteristics);
    }

    private void SubscriptionsComplete()
    {
        Connected = true;
        _connectionCompletion?.TrySetResult(true);
        var preferences = _activity.GetSharedPreferences("annotape-meter", FileCreationMode.Private);
        var token = ReadPreference(preferences, "token");
        var deviceId = ReadPreference(preferences, "device-id");
        _auth?.Dispose();
        _auth = new MijiaStandardAuth(WriteCharacteristic, message => StatusChanged?.Invoke(message),
            keys => AuthenticationComplete(preferences, keys),
            () => preferences?.Edit()?.Clear()?.Apply());
        _auth.Start(token, deviceId);
    }

    private void WriteCharacteristic(string uuid, byte[] value)
    {
        lock (_writeSync)
        {
            _writes.Enqueue((uuid, value.ToArray()));
            if (_writeLoopRunning) return;
            _writeLoopRunning = true;
        }
        _ = ProcessWritesAsync();
    }

    private async Task ProcessWritesAsync()
    {
        try
        {
            while (true)
            {
                (string Uuid, byte[] Value) request;
                lock (_writeSync)
                {
                    if (!_writes.TryDequeue(out request))
                    {
                        _writeLoopRunning = false;
                        return;
                    }
                }
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _activity.RunOnUiThread(() =>
                {
                    try
                    {
                        WriteCharacteristicNow(request.Uuid, request.Value);
                        completion.TrySetResult();
                    }
                    catch (Exception exception) { completion.TrySetException(exception); }
                });
                await completion.Task;
                await Task.Delay(60);
            }
        }
        catch (Exception exception)
        {
            lock (_writeSync)
            {
                _writes.Clear();
                _writeLoopRunning = false;
            }
            StatusChanged?.Invoke($"Meter write failed: {exception.Message}");
        }
    }

    private void WriteCharacteristicNow(string uuid, byte[] value)
    {
        if (_gatt is null || !_characteristics.TryGetValue(uuid, out var characteristic))
            throw new IOException($"The meter is missing GATT characteristic {uuid}.");
        global::Android.Util.Log.Info(LogTag, $"write {uuid} length={value.Length}");
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            if (_gatt.WriteCharacteristic(characteristic, value, (int)GattWriteType.NoResponse) != 0)
                throw new IOException($"The meter rejected a write to {uuid}.");
            return;
        }
#pragma warning disable CA1422
        characteristic.WriteType = GattWriteType.NoResponse;
        characteristic.SetValue(value);
        if (!_gatt.WriteCharacteristic(characteristic))
            throw new IOException($"The meter rejected a write to {uuid}.");
#pragma warning restore CA1422
    }

    private void OnCharacteristicChanged(BluetoothGattCharacteristic characteristic, byte[] value)
    {
        var uuid = characteristic.Uuid?.ToString() ?? "";
        if (uuid.StartsWith("00000010-", StringComparison.OrdinalIgnoreCase)
            || uuid.StartsWith("00000019-", StringComparison.OrdinalIgnoreCase))
        {
            global::Android.Util.Log.Info(LogTag, $"authentication notification {uuid} length={value.Length}");
            _auth?.Handle(uuid, value);
            return;
        }
        global::Android.Util.Log.Info(LogTag, $"notification {uuid} length={value.Length}");
        if (!_authenticated) return;
        var readingValue = value;
        if (uuid.StartsWith("0000fea8-", StringComparison.OrdinalIgnoreCase)
            && _sessionKeys is not null
            && TryDecryptSessionPacket(value, _sessionKeys, out var decrypted))
        {
            readingValue = decrypted;
            if (IsHotoReport(decrypted))
            {
                WriteCharacteristic("0000fea7-0000-1000-8000-00805f9b34fb",
                    EncryptSessionPacket([(byte)(decrypted[0] | 0x10), decrypted[1]], _sessionKeys));
                if (TryDecodeHotoReading(decrypted, out var hotoMillimetres))
                    MeasurementReceived?.Invoke(new LaserMeasureReading(hotoMillimetres, _deviceName));
                return;
            }
        }
        if (!TryDecodePlainReading(readingValue, out var millimetres)) return;
        MeasurementReceived?.Invoke(new LaserMeasureReading(millimetres, _deviceName));
    }

    private void AuthenticationComplete(global::Android.Content.ISharedPreferences? preferences, MijiaStandardAuth.AuthKeys keys)
    {
        preferences?.Edit()
            ?.PutString("token", Convert.ToBase64String(keys.Token))
            ?.PutString("device-id", keys.DeviceId is null ? null : Convert.ToBase64String(keys.DeviceId))
            ?.Apply();
        _authenticated = true;
        _sessionKeys = keys;
        _appFrameCounter = 0;
        StatusChanged?.Invoke($"{_deviceName} is connected privately. Take a measurement.");
        global::Android.Util.Log.Info(LogTag, "Local authentication succeeded");
    }

    private static byte[]? ReadPreference(global::Android.Content.ISharedPreferences? preferences, string key)
    {
        var value = preferences?.GetString(key, null);
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { return null; }
    }

    private static bool TryDecryptSessionPacket(byte[] packet, MijiaStandardAuth.AuthKeys keys, out byte[] plaintext)
    {
        plaintext = [];
        if (packet.Length < 7) return false;
        var nonce = new byte[12];
        keys.DeviceIv.CopyTo(nonce, 0);
        packet.AsSpan(0, 2).CopyTo(nonce.AsSpan(8));
        var ciphertext = packet.AsSpan(2, packet.Length - 6);
        var tag = packet.AsSpan(packet.Length - 4, 4);
        var padded = new byte[ciphertext.Length];
        try
        {
            using var cipher = new AesCcm(keys.DeviceKey);
            cipher.Decrypt(nonce, ciphertext, tag, padded);
            plaintext = padded;
            return true;
        }
        catch (CryptographicException) { return false; }
    }

    private byte[] EncryptSessionPacket(byte[] plaintext, MijiaStandardAuth.AuthKeys keys)
    {
        var counter = Interlocked.Increment(ref _appFrameCounter);
        var nonce = new byte[12];
        keys.AppIv.CopyTo(nonce, 0);
        BitConverter.TryWriteBytes(nonce.AsSpan(8), counter);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[4];
        using (var cipher = new AesCcm(keys.AppKey)) cipher.Encrypt(nonce, plaintext, ciphertext, tag);
        return [(byte)counter, (byte)(counter >> 8), .. ciphertext, .. tag];
    }

    private static bool IsHotoReport(byte[] plaintext) =>
        plaintext.Length >= 4 && (plaintext[0] & 0x10) == 0 && plaintext[3] == 0x80;

    private static bool TryDecodeHotoReading(byte[] plaintext, out decimal millimetres)
    {
        millimetres = 0;
        for (var offset = 4; offset + 2 <= plaintext.Length;)
        {
            var type = plaintext[offset];
            var width = type switch
            {
                <= 2 => 1,
                3 or 4 => 2,
                5 or 6 or 9 => 4,
                7 or 8 or 0x0a => 8,
                _ => 0
            };
            if (width == 0 || offset + 2 + width > plaintext.Length) return false;
            long value = 0;
            for (var index = 0; index < width; index++)
                value = value << 8 | plaintext[offset + 2 + index];
            if (type == 4 && plaintext[offset + 1] == 0)
            {
                millimetres = value;
                return true;
            }
            offset += 2 + width;
        }
        return false;
    }

    // Some meters expose an unencrypted text value. Xiaomi-bound binary packets are intentionally
    // left undecoded until a real device trace establishes their authenticated wire format.
    private static bool TryDecodePlainReading(byte[] value, out decimal millimetres)
    {
        millimetres = 0;
        if (value.Length is 0 or > 64 || value.Any(item => item is < 0x09 or > 0x7e)) return false;
        var match = PlainReadingPattern().Match(Encoding.ASCII.GetString(value));
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var amount)) return false;
        millimetres = match.Groups[2].Value.ToLowerInvariant() switch
        {
            "mm" => amount,
            "cm" => amount * 10m,
            "m" => amount * 1000m,
            "ft" => amount * 304.8m,
            _ => 0
        };
        return millimetres >= 0;
    }

    private void FailConnection(string message)
    {
        _connectionCompletion?.TrySetException(new IOException(message));
        StatusChanged?.Invoke(message);
    }

    private static string Hex(byte[]? value) => value is null ? "" : Convert.ToHexString(value);

    public void Dispose()
    {
        _ = DisconnectAsync();
        _permissionCompletion?.TrySetCanceled();
    }

    [GeneratedRegex(@"^\s*([0-9]+(?:\.[0-9]+)?)\s*(mm|cm|m|ft)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlainReadingPattern();

    private sealed class MeterScanCallback(HotoLaserMeasureClient owner) : ScanCallback
    {
        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            if (result is not null) owner.OnScanResult(result);
        }

        public override void OnScanFailed(ScanFailure errorCode) =>
            owner._scanCompletion?.TrySetException(new IOException($"BLE scan failed ({errorCode})."));
    }

    private sealed class MeterGattCallback(HotoLaserMeasureClient owner) : BluetoothGattCallback
    {
        private readonly Queue<BluetoothGattCharacteristic> _subscriptions = new();

        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            if (gatt is not null) owner.OnConnectionStateChange(gatt, status, newState);
        }

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
        {
            if (gatt is not null) owner.OnServicesDiscovered(gatt, status, this);
        }

        public void BeginSubscriptions(BluetoothGatt gatt, IEnumerable<BluetoothGattCharacteristic> characteristics)
        {
            foreach (var characteristic in characteristics) _subscriptions.Enqueue(characteristic);
            SubscribeNext(gatt);
        }

        private void SubscribeNext(BluetoothGatt gatt)
        {
            while (_subscriptions.TryDequeue(out var characteristic))
            {
                if (!gatt.SetCharacteristicNotification(characteristic, true)) continue;
                var descriptor = characteristic.GetDescriptor(ClientConfigurationUuid);
                if (descriptor is null) continue;
                var configuration = ((characteristic.Properties & GattProperty.Indicate) != 0
                    ? BluetoothGattDescriptor.EnableIndicationValue
                    : BluetoothGattDescriptor.EnableNotificationValue)?.ToArray() ?? [];
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    if (gatt.WriteDescriptor(descriptor, configuration) == 0) return;
                }
                else
                {
                    descriptor.SetValue(configuration);
                    if (gatt.WriteDescriptor(descriptor)) return;
                }
            }
            owner.SubscriptionsComplete();
        }

        public override void OnDescriptorWrite(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, GattStatus status)
        {
            if (gatt is not null) SubscribeNext(gatt);
        }

#pragma warning disable CA1422
        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
        {
            if (characteristic?.GetValue() is { } value) owner.OnCharacteristicChanged(characteristic, value);
        }
#pragma warning restore CA1422

        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value) =>
            owner.OnCharacteristicChanged(characteristic, value);
    }
}
