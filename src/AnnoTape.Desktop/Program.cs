using AnnoTape.App;
using AnnoTape.DesktopHost;
using CupriFace.Shell;

var capabilities = new DesktopPlatformCapabilities();
DesktopHost.Run(new AnnoTapeApp(capabilities));
