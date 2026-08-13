namespace Deskto.Core.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        ConfigTests.Register();
        GeometryTests.Register();
        return TestHarness.RunAll();
    }
}
