using System.Reflection;

namespace DocumentFinder.App;

internal static class AppBrand
{
    public const string Name="모두찾아줘";
    public static Version Version=>Assembly.GetExecutingAssembly().GetName().Version??new Version(1,1,0);
    public static string VersionText=>$"{Version.Major}.{Version.Minor}.{Version.Build}";
    public static Icon LoadIcon()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ModuFind.Icon")!;
        return new Icon(stream);
    }
}
