namespace AnnoTape.App.Platform;

public sealed record LaserMeasureReading(decimal Millimetres, string DeviceName);

public interface ILaserMeasureCapabilities
{
    event Action<string>? LaserMeasureStatusChanged;
    event Action<LaserMeasureReading>? LaserMeasurementReceived;
    bool LaserMeasureConnected { get; }
    bool LaserMeasureReady { get; }
    Task ConnectLaserMeasureAsync(CancellationToken cancellationToken = default);
    Task DisconnectLaserMeasureAsync();
}
