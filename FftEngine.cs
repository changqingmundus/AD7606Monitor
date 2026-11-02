using System.Numerics;

namespace Ad7606Host;

internal static class FftEngine
{
    /// <summary>原地基-2 正变换。要求长度为 2 的幂。</summary>
    public static void Forward(Complex[] data)
    {
        int n = data.Length;
        if (n <= 1 || (n & (n - 1)) != 0) return;

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            var wLen = new Complex(Math.Cos(ang), Math.Sin(ang));
            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                int half = len >> 1;
                for (int j = 0; j < half; j++)
                {
                    var u = data[i + j];
                    var v = data[i + j + half] * w;
                    data[i + j] = u + v;
                    data[i + j + half] = u - v;
                    w *= wLen;
                }
            }
        }
    }
}