using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.FString {
    static class EnumNameCache<TEnum> {
        public static readonly Dictionary<TEnum, string> Cache;

        static EnumNameCache () {
            var values = Enum.GetValues(typeof(TEnum));
            var names = Enum.GetNames(typeof(TEnum));
            Cache = new Dictionary<TEnum, string>(values.Length);
            for (int i = 0; i < values.Length; i++) {
                var value = (TEnum)values.GetValue(i);
                var name = names[i];
                Cache[value] = name;
            }
        }
    }

    public static class HashUtil {
        static ThreadLocal<SHA256> Hasher = new ThreadLocal<SHA256>(() => SHA256.Create());
        static ThreadLocal<StringBuilder> StringBuilder = new ThreadLocal<StringBuilder>(() => new StringBuilder());

        public static string GetShortHash (string text) {
            return GetShortHash(Encoding.UTF8.GetBytes(text));
        }

        public static string GetShortHash (byte[] bytes) {
            return GetHashString(bytes, 0, bytes.Length);
        }

        public static string GetHashString (byte[] bytes, int offset, int count, int hashLength = 8) {
            var hash = Hasher.Value;
            var sb = StringBuilder.Value;
            sb.Clear();
            var hashBytes = hash.ComputeHash(bytes, offset, count);
            hashLength = Math.Min(hashBytes.Length, hashLength);
            for (int i = 0; i < hashLength; i++) {
                var b = hashBytes[i];
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
