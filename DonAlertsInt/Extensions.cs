using System.Text;

namespace DonAlertsInt;

public static class Extensions
{
    public static byte[] AsUtf8(this string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }
}