namespace AionDPS.Combat;

/// <summary>Welch's t-test of "B minus A": the difference, its standard error, t, the Welch-Satterthwaite degrees of
/// freedom, the two-sided p and the 95 % interval of the difference.</summary>
public readonly record struct WelchResult(double Difference, double StandardError, double T, double Df, double P, double Low, double High)
{
    /// <summary>True when the interval does not contain 0.</summary>
    public bool Significant => Low > 0 || High < 0;
}

/// <summary>
/// The small statistics kit behind the dummy test's noise statement: mean, sample SD, median and Welch's t-test with the exact Student t distribution (regularized incomplete beta,
/// Numerical Recipes' continued fraction; the normal only for df above 2000 where the two cannot be told apart). Pure and self-contained, checked against hand-computed values in
/// <see cref="SelfCheckDummy"/>. Taken from Stacy's fork (StacysView/Aion-DPS-Meter, MIT, 23 October 2026); the Wilson interval, the two-proportion test and the line fit are left out until
/// something needs them.
/// </summary>
public static class DummyStatistics
{
    /// <summary>The z of a 95 % two-sided interval under the normal.</summary>
    public const double Z95 = 1.959963984540054;

    /// <summary>Mean; 0 for an empty list (callers check the count first).</summary>
    public static double Mean(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double sum = 0;
        foreach (double v in values)
        {
            sum += v;
        }

        return sum / values.Count;
    }

    /// <summary>Sample standard deviation (n - 1 in the divisor); 0 below two values.</summary>
    public static double SampleSd(IReadOnlyList<double> values) => Math.Sqrt(SampleVariance(values));

    public static double SampleVariance(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        double mean = Mean(values);
        double sum = 0;
        foreach (double v in values)
        {
            sum += (v - mean) * (v - mean);
        }

        return sum / (values.Count - 1);
    }

    /// <summary>Median (the mean of the two middle values for an even count); 0 for an empty list.</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>Two-sided p of a Student t with <paramref name="df"/> degrees of freedom: P(|T| &gt;= |t|).</summary>
    public static double StudentTwoSidedP(double t, double df)
    {
        if (double.IsNaN(t) || df <= 0)
        {
            return 1;
        }

        if (double.IsInfinity(t))
        {
            return 0;
        }

        if (df > 2000)
        {
            return NormalTwoSidedP(t);
        }

        return RegularizedBeta(df / 2, 0.5, df / (df + t * t));
    }

    /// <summary>The t that leaves <c>1 - confidence</c> in the two tails (2.228 for df 10 at 95 %). Bisection on
    /// <see cref="StudentTwoSidedP"/>; the normal quantile for df above 2000.</summary>
    public static double StudentCritical(double df, double confidence = 0.95)
    {
        double alpha = 1 - confidence;
        if (df > 2000)
        {
            // Normal quantile by the same bisection on the normal tail.
            return Bisect(z => NormalTwoSidedP(z), alpha);
        }

        return Bisect(t => StudentTwoSidedP(t, Math.Max(df, 0.1)), alpha);
    }

    private static double Bisect(Func<double, double> twoSidedP, double alpha)
    {
        double lo = 0, hi = 1;
        while (twoSidedP(hi) > alpha && hi < 1e7)
        {
            hi *= 2;
        }

        for (int i = 0; i < 100; i++)
        {
            double mid = (lo + hi) / 2;
            if (twoSidedP(mid) > alpha)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (lo + hi) / 2;
    }

    /// <summary>P(|Z| &gt;= |z|) under the standard normal (erfc, Numerical Recipes' Chebyshev fit, error about 1e-7).</summary>
    public static double NormalTwoSidedP(double z)
    {
        double x = Math.Abs(z) / Math.Sqrt(2);
        double t = 1 / (1 + 0.5 * x);
        double poly = -x * x - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 + t * (-0.18628806
            + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 + t * 0.17087277))))))));
        return Math.Min(1, t * Math.Exp(poly));
    }

    /// <summary>Welch's t-test of B against A from summary numbers (sample SDs). Null below two values on a side.
    /// The interval is the difference +/- the Student critical value at the Welch-Satterthwaite df times the error.</summary>
    public static WelchResult? Welch(double meanA, double sdA, int nA, double meanB, double sdB, int nB, double confidence = 0.95)
    {
        if (nA < 2 || nB < 2)
        {
            return null;
        }

        double diff = meanB - meanA;
        double va = sdA * sdA / nA;
        double vb = sdB * sdB / nB;
        double se = Math.Sqrt(va + vb);
        if (se <= 0)
        {
            // No spread at all on either side: the difference is exact.
            double dfExact = nA + nB - 2;
            return new WelchResult(diff, 0, diff == 0 ? 0 : (diff > 0 ? double.PositiveInfinity : double.NegativeInfinity), dfExact, diff == 0 ? 1 : 0, diff, diff);
        }

        double t = diff / se;
        double df = (va + vb) * (va + vb) / (va * va / (nA - 1) + vb * vb / (nB - 1));
        double p = StudentTwoSidedP(t, df);
        double crit = StudentCritical(df, confidence);
        return new WelchResult(diff, se, t, df, p, diff - crit * se, diff + crit * se);
    }

    /// <summary>Welch's t-test on two raw samples.</summary>
    public static WelchResult? Welch(IReadOnlyList<double> a, IReadOnlyList<double> b) =>
        Welch(Mean(a), SampleSd(a), a.Count, Mean(b), SampleSd(b), b.Count);

    /// <summary>Regularized incomplete beta function I_x(a, b) (Numerical Recipes betai).</summary>
    private static double RegularizedBeta(double a, double b, double x)
    {
        if (x <= 0)
        {
            return 0;
        }

        if (x >= 1)
        {
            return 1;
        }

        double bt = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
        return x < (a + 1) / (a + b + 2)
            ? bt * BetaContinuedFraction(a, b, x) / a
            : 1 - bt * BetaContinuedFraction(b, a, 1 - x) / b;
    }

    /// <summary>Continued fraction of the incomplete beta by the modified Lentz method.</summary>
    private static double BetaContinuedFraction(double a, double b, double x)
    {
        const double Tiny = 1e-300;
        double qab = a + b, qap = a + 1, qam = a - 1;
        double c = 1;
        double d = 1 - qab * x / qap;
        if (Math.Abs(d) < Tiny)
        {
            d = Tiny;
        }

        d = 1 / d;
        double h = d;
        for (int m = 1; m <= 1000; m++)
        {
            int m2 = 2 * m;
            double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d;
            if (Math.Abs(d) < Tiny)
            {
                d = Tiny;
            }

            c = 1 + aa / c;
            if (Math.Abs(c) < Tiny)
            {
                c = Tiny;
            }

            d = 1 / d;
            h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d;
            if (Math.Abs(d) < Tiny)
            {
                d = Tiny;
            }

            c = 1 + aa / c;
            if (Math.Abs(c) < Tiny)
            {
                c = Tiny;
            }

            d = 1 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < 3e-14)
            {
                break;
            }
        }

        return h;
    }

    private static readonly double[] LanczosCoefficients =
    {
        0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
        -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
    };

    /// <summary>ln Gamma(x) for x &gt; 0 (Lanczos, g = 7).</summary>
    private static double LogGamma(double x)
    {
        if (x < 0.5)
        {
            return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1 - x);
        }

        x -= 1;
        double a = LanczosCoefficients[0];
        double t = x + 7.5;
        for (int i = 1; i < 9; i++)
        {
            a += LanczosCoefficients[i] / (x + i);
        }

        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }
}
