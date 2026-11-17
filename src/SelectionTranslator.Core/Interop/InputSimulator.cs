using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SelectionTranslator.Core.Interop;

public interface IInputSimulator
{
    void SendCopy();
}

public sealed class InputSimulator : IInputSimulator
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkShift = 0x10;
    private const ushort VkLWin = 0x5B;
    private const ushort VkRWin = 0x5C;
    private const ushort VkC = 0x43;

    public void SendCopy()
    {
        var inputs = new[]
        {
            Key(VkControl, up: true),
            Key(VkShift, up: true),
            Key(VkMenu, up: true),
            Key(VkLWin, up: true),
            Key(VkRWin, up: true),
            Key(VkControl),
            Key(VkC),
            Key(VkC, up: true),
            Key(VkControl, up: true)
        };

        if (NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "模拟复制失败，目标程序可能以管理员权限运行");
    }

    private static NativeMethods.INPUT Key(ushort virtualKey, bool up = false) => new()
    {
        type = InputKeyboard,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = virtualKey,
                wScan = (ushort)(NativeMethods.MapVirtualKey(virtualKey, 0) & 0xFF),
                dwFlags = up ? KeyEventKeyUp : 0
            }
        }
    };
}