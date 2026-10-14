using System.Security.Cryptography;
using System.Text;

namespace BookingApi.Infrastructure.Suppliers.SkyLink;

public static class SkyLinkSign
{
    public static string Compute(string partnerId, string partnerKey) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(partnerId + partnerKey))).ToLowerInvariant();
}
