using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AnnoTape.App;
using CupriFace;
using CupriFace.Android;

namespace AnnoTape.AndroidHost;

[Activity(
    Label = "AnnoTape",
    Name = "app.annotape.mobile.MainActivity",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : CupriActivity
{
    private AndroidPlatformCapabilities? _capabilities;
    private AnnoTapeApp? _app;
    private bool _recreatingAfterPhotoFlow;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        global::Android.Util.Log.Info("annotape", "MainActivity OnCreate starting.");
        base.OnCreate(savedInstanceState);
        global::Android.Util.Log.Info("annotape", "MainActivity OnCreate completed.");
    }

    protected override CupriApp CreateApp()
    {
        _capabilities = new AndroidPlatformCapabilities(this);
        _app = new AnnoTapeApp(_capabilities);
        _ = _app.Initialization.ContinueWith(_ =>
        {
            if (_app.InitializationError is { } error)
                global::Android.Util.Log.Error("annotape", $"Background startup failed: {error}");
            else
                global::Android.Util.Log.Info("annotape", "Background startup completed.");
        }, TaskScheduler.Default);
        _app.ExternalPhotoFlowCompleted += () => RunOnUiThread(() =>
        {
            if (IsFinishing || IsDestroyed) return;
            _recreatingAfterPhotoFlow = true;
            global::Android.Util.Log.Info("annotape", "Recreating Activity after completed photo flow.");
            Recreate();
        });
        return _app;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        _capabilities?.HandleActivityResult(requestCode, resultCode, data);
    }

    protected override void OnPause()
    {
        if (_app is not null && !_recreatingAfterPhotoFlow) _ = _app.FlushAsync();
        else if (_recreatingAfterPhotoFlow)
            global::Android.Util.Log.Info("annotape", "Skipping redundant pause flush during photo-flow recreation.");
        base.OnPause();
    }

}
