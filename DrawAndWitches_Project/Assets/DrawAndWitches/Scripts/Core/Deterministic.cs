using System;

namespace DrawAndWitches.Core
{
    /// <summary>
    /// 決定論的乱数生成器（xorshift32）。Web版 DeterministicRng と同一の数列を返す。
    /// UnityEngine.Random や System.Random は再現性が保証されないため、ロジックでは必ずこのクラスを使う。
    /// </summary>
    public sealed class DeterministicRng
    {
        private const uint ZeroSeedReplacement = 0x9E3779B9u;
        private const int ShiftA = 13;
        private const int ShiftB = 17;
        private const int ShiftC = 5;

        private uint state;

        public DeterministicRng(uint seed)
        {
            state = seed == 0u ? ZeroSeedReplacement : seed;
        }

        public uint NextUint()
        {
            uint x = state;
            x ^= x << ShiftA;
            x ^= x >> ShiftB;
            x ^= x << ShiftC;
            state = x;
            return x;
        }

        /// <summary>0以上 max 未満の整数（max は1以上）</summary>
        public int NextInt(int max)
        {
            if (max < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(max), max, "DeterministicRng.NextInt: max must be >= 1");
            }
            return (int)(NextUint() % (uint)max);
        }

        /// <summary>新しい seed を生成する（生成自体は非決定でよい。生成後はリプレイへ保存される）</summary>
        public static uint CreateSeed()
        {
            byte[] buffer = new byte[sizeof(uint)];
            using (System.Security.Cryptography.RandomNumberGenerator generator = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                generator.GetBytes(buffer);
            }
            uint seed = BitConverter.ToUInt32(buffer, 0);
            return seed == 0u ? 1u : seed;
        }
    }

    /// <summary>FNV-1a 32bit ハッシュと、JS版と一致させるための数値ユーティリティ。</summary>
    public static class Fnv1a
    {
        public const uint Offset = 0x811C9DC5u;
        private const uint Prime = 0x01000193u;
        private const int BytesPerInt = 4;
        private const int BitsPerByte = 8;
        private const uint ByteMask = 0xFFu;
        private const double QuantizeScale = 256.0;
        private const double Half = 0.5;
        private const int HexDigits = 8;

        public static uint MixInt(uint hash, int value)
        {
            uint h = hash;
            uint v = unchecked((uint)value);
            for (int i = 0; i < BytesPerInt; i++)
            {
                h ^= (v >> (i * BitsPerByte)) & ByteMask;
                h = unchecked(h * Prime);
            }
            return h;
        }

        /// <summary>UTF-16 コード単位ごとに下位・上位バイトを混ぜる（JS版 fnvString と同一）</summary>
        public static uint MixString(string text)
        {
            uint h = Offset;
            string s = text ?? string.Empty;
            for (int i = 0; i < s.Length; i++)
            {
                uint c = s[i];
                h ^= c & ByteMask;
                h = unchecked(h * Prime);
                h ^= (c >> BitsPerByte) & ByteMask;
                h = unchecked(h * Prime);
            }
            return h;
        }

        /// <summary>四捨五入（JS の Math.floor(x+0.5) と同一。銀行丸めは使わない）</summary>
        public static int RoundHalfUp(double value)
        {
            return ToInt32Wrapped(Math.Floor(value + Half));
        }

        /// <summary>座標をハッシュ用に 1/256px の固定小数点へ変換</summary>
        public static int Quantize(double value)
        {
            return ToInt32Wrapped(Math.Floor(value * QuantizeScale + Half));
        }

        /// <summary>JS の ToInt32（2^32 を法とした切り詰め）と同じ変換</summary>
        public static int ToInt32Wrapped(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }
            double truncated = Math.Truncate(value);
            const double TwoPow32 = 4294967296.0;
            double modulo = truncated % TwoPow32;
            if (modulo < 0)
            {
                modulo += TwoPow32;
            }
            return unchecked((int)(uint)modulo);
        }

        public static string ToHex(uint hash)
        {
            return hash.ToString("x" + HexDigits.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        }

        public static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}

/*
## このアセットの役割
決定論乱数（xorshift32）・FNV-1a ハッシュ・JS互換の丸め処理を提供します。リプレイ再現性の土台です。

## 他アセットとの依存関係
BattleSimulation / BotPlanner / StageValidator / ReplaySerializer から使用されます。Web版 js/core/rng.js・hash.js と同一結果を返します。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。

## 注意点
UnityEngine.Random・Mathf（float）をロジックで使うと Web版と結果が一致しなくなります。必ず double とこのクラスを使ってください。
*/
