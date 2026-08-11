namespace Deskup.Core.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        ConfigTests.Register();
        FileTests.Register();
        GeometryTests.Register();
        return TestHarness.RunAll();
    }
}
