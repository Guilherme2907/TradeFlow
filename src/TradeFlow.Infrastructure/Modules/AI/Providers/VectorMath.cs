using System.Globalization;
using System.Text;

namespace TradeFlow.Infrastructure.Modules.AI.Providers;

internal static class VectorMath
{
    public static float[] NormalizeAndResize(ReadOnlySpan<float> input, int targetDimensions)
    {
        var output = new float[targetDimensions];
        var copyLength = Math.Min(input.Length, targetDimensions);
        input[..copyLength].CopyTo(output);

        double sumSquares = 0;
        for (var i = 0; i < output.Length; i++)
        {
            sumSquares += (double)output[i] * output[i];
        }

        if (sumSquares > 1e-12)
        {
            var invNorm = (float)(1.0 / Math.Sqrt(sumSquares));
            for (var i = 0; i < output.Length; i++)
            {
                output[i] *= invNorm;
            }
        }

        return output;
    }

    public static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var len = Math.Min(a.Length, b.Length);
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < len; i++)
        {
            dot += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom > 1e-12 ? dot / denom : 0.0;
    }

    public static string FormatPgVectorLiteral(ReadOnlySpan<float> vector)
    {
        var sb = new StringBuilder(vector.Length * 10 + 2);
        sb.Append('[');
        for (var i = 0; i < vector.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(vector[i].ToString("G9", CultureInfo.InvariantCulture));
        }

        sb.Append(']');
        return sb.ToString();
    }
}
