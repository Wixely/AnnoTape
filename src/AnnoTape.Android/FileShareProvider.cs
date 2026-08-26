using Android.App;
using Android.Content;
using AndroidX.Core.Content;

namespace AnnoTape.AndroidHost;

[ContentProvider(
    ["app.annotape.mobile.files"],
    Exported = false,
    GrantUriPermissions = true,
    Name = "annotape.androidhost.FileShareProvider")]
[MetaData("android.support.FILE_PROVIDER_PATHS", Resource = "@xml/file_paths")]
public sealed class FileShareProvider : FileProvider;
