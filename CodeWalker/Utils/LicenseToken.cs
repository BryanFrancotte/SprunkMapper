using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CodeWalker.Utils
{
    /// <summary>
    /// What a licence key says: who it was issued to, when, and its id (used for revocation).
    /// </summary>
    public sealed class LicenseInfo
    {
        public uint Id;
        public string Name;
        public DateTime Issued;

        public string IdHex { get { return Id.ToString("x8"); } }
    }

    /// <summary>
    /// The licence key format, shared by SprunkMapper (verify) and Tools/SprunkKeyGen (sign).
    /// A key is "SPRK1-" + base64url(payload + signature), where the payload is
    /// [format version:1][id:4 LE][issued, days since 2026-01-01:2 LE][name length:1][name UTF-8]
    /// and the signature is ECDSA P-256 / SHA-256 over the payload (64 bytes, r||s).
    /// No WinForms or app dependencies here: the key generator compiles this file too.
    /// </summary>
    public static class LicenseToken
    {
        public const string Prefix = "SPRK1-";
        public const byte FormatVersion = 1;
        public const int SignatureLength = 64;
        public const int CoordinateLength = 32;
        public const int MaxNameBytes = 32;
        static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static byte[] BuildPayload(uint id, string name, DateTime issuedUtc)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name ?? "");
            if (nameBytes.Length == 0) throw new ArgumentException("The name can't be empty.");
            if (nameBytes.Length > MaxNameBytes) throw new ArgumentException("The name is too long (" + MaxNameBytes + " bytes max).");
            var days = (int)Math.Floor((issuedUtc - Epoch).TotalDays);
            if (days < 0 || days > ushort.MaxValue) throw new ArgumentException("The issue date is out of range.");

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(FormatVersion);
                bw.Write(id);
                bw.Write((ushort)days);
                bw.Write((byte)nameBytes.Length);
                bw.Write(nameBytes);
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static string Encode(byte[] payload, byte[] signature)
        {
            if (signature == null || signature.Length != SignatureLength) throw new ArgumentException("Bad signature length.");
            var all = new byte[payload.Length + signature.Length];
            Buffer.BlockCopy(payload, 0, all, 0, payload.Length);
            Buffer.BlockCopy(signature, 0, all, payload.Length, signature.Length);
            return Prefix + Base64UrlEncode(all);
        }

        /// <summary>Removes the whitespace and line breaks a pasted key may pick up.</summary>
        public static string Normalize(string text)
        {
            if (text == null) return "";
            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Decodes, checks the signature against the given public key, and reads the payload.
        /// Returns null on success, or a short reason the key isn't valid.
        /// </summary>
        public static string Validate(string text, byte[] publicX, byte[] publicY, out LicenseInfo info)
        {
            info = null;
            var key = Normalize(text);
            if (key.Length == 0) return "No key entered.";
            if (!key.StartsWith(Prefix, StringComparison.Ordinal)) return "That isn't a SprunkMapper key (it should start with " + Prefix + ").";

            byte[] all = Base64UrlDecode(key.Substring(Prefix.Length));
            if (all == null || all.Length <= SignatureLength + 8) return "The key is incomplete or damaged.";

            var payload = new byte[all.Length - SignatureLength];
            var signature = new byte[SignatureLength];
            Buffer.BlockCopy(all, 0, payload, 0, payload.Length);
            Buffer.BlockCopy(all, payload.Length, signature, 0, SignatureLength);

            if (!VerifySignature(payload, signature, publicX, publicY)) return "The key is not valid.";

            var error = ParsePayload(payload, out info);
            if (error != null) info = null;
            return error;
        }

        static string ParsePayload(byte[] payload, out LicenseInfo info)
        {
            info = null;
            try
            {
                using (var br = new BinaryReader(new MemoryStream(payload)))
                {
                    var ver = br.ReadByte();
                    if (ver != FormatVersion) return "This key is for a different version of SprunkMapper.";
                    var id = br.ReadUInt32();
                    var days = br.ReadUInt16();
                    var len = br.ReadByte();
                    var nameBytes = br.ReadBytes(len);
                    if (nameBytes.Length != len || br.BaseStream.Position != payload.Length) return "The key is incomplete or damaged.";
                    info = new LicenseInfo
                    {
                        Id = id,
                        Name = Encoding.UTF8.GetString(nameBytes),
                        Issued = Epoch.AddDays(days),
                    };
                    return null;
                }
            }
            catch (EndOfStreamException)
            {
                return "The key is incomplete or damaged.";
            }
        }

        public static bool VerifySignature(byte[] payload, byte[] signature, byte[] publicX, byte[] publicY)
        {
            try
            {
                var p = new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = publicX, Y = publicY },
                };
                using (var ecdsa = ECDsa.Create(p))
                {
                    return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256);
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        public static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static byte[] Base64UrlDecode(string text)
        {
            var s = text.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: return null;
            }
            try
            {
                return Convert.FromBase64String(s);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
