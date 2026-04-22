using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct CGRect
{
    public CGPoint Origin;
    public CGSize Size;
}
