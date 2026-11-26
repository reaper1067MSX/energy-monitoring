using System.Runtime.CompilerServices;

namespace VoltTray.Infrastructure.Hid.Interop;

/// <summary>
/// Inline fixed-size buffer representing the 17 reserved <see cref="ushort"/> words in <see cref="HidCapabilities"/>.
/// </summary>
[InlineArray(HidNativeMethods.HidCapabilitiesReservedWordCount)]
internal struct HidCapabilitiesReservedBuffer
{
    private ushort _element0;
}
