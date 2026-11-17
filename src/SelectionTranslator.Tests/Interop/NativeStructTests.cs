using System.Runtime.InteropServices;
using SelectionTranslator.Core.Interop;

namespace SelectionTranslator.Tests.Interop;

public class NativeStructTests
{
    [Fact]
    public void Input_struct_matches_windows_size()
    {
        var expected = IntPtr.Size == 8 ? 40 : 28;
        Assert.Equal(expected, Marshal.SizeOf<NativeMethods.INPUT>());
    }
}