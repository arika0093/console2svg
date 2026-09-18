using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ConsoleToSvg.Recording;

/// <summary>
/// Best-effort PTY slave echo control. Toggling termios ECHO flags via the
/// controller fd affects the slave side on Linux/macOS.
/// </summary>
internal static class PtyEcho
{
    // Attempt to set PTY slave ECHO state so that input bytes forwarded from the outer
    // terminal (e.g. OSC color-query responses) can be suppressed when needed.
    internal static void TrySetPtyEcho(Stream controllerStream, bool enabled)
    {
        if (
            !RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && !RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD)
        )
        {
            return;
        }

        try
        {
            // The PTY backend wraps the controller fd in a FileStream on Unix.
            if (controllerStream is not FileStream fs)
            {
                return;
            }

            var fd = fs.SafeFileHandle;
            if (fd.IsInvalid)
            {
                return;
            }

            if (tcgetattr(fd, out var t) != 0)
            {
                return;
            }

            // Toggle echo-related flags on the PTY slave (tcsetattr on the controller fd
            // modifies the slave's termios settings on Linux/macOS).
            const uint ECHO = 0x0008u;
            const uint ECHOE = 0x0010u;
            const uint ECHOK = 0x0020u;
            const uint ECHONL = 0x0040u;
            const uint ECHOCTL = 0x0200u;
            const uint flags = ECHO | ECHOE | ECHOK | ECHONL | ECHOCTL;
            if (enabled)
            {
                t.c_lflag |= flags;
            }
            else
            {
                t.c_lflag &= ~flags;
            }
            tcsetattr(
                fd,
                0 /* TCSANOW */
                ,
                ref t
            );
        }
        catch
        {
            // Best-effort; ignore failures.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Termios
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        public byte c_line;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] c_cc;

        public uint c_ispeed;
        public uint c_ospeed;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(SafeFileHandle fd, out Termios termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(
        SafeFileHandle fd,
        int optional_actions,
        ref Termios termios
    );
}
