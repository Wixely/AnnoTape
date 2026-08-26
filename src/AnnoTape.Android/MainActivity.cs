using Android.App;
using Android.Content;
using Android.Content.PM;
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

    protected override CupriApp CreateApp()
    {
        _capabilities = new AndroidPlatformCapabilities(this);
        _app = new AnnoTapeApp(_capabilities);
        _app.ExternalPhotoFlowCompleted += () => RunOnUiThread(() =>
        {
            if (!IsFinishing && !IsDestroyed) Recreate();
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
        if (_app is not null) _ = _app.FlushAsync();
        base.OnPause();
    }

}
