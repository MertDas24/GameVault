using System.Globalization;

namespace GameVault.Web.Infrastructure;

/// <summary>Bir sütun grafiğindeki seri: görünen adı ve rengini veren CSS sınıfı (renk satır içi verilmez; CSP).</summary>
public sealed record ChartSeries(string Name, string CssClass);

/// <summary>X eksenindeki bir dilim: eksen etiketi, araç ipucu/tablo için tam adı ve her serinin değeri.</summary>
public sealed record ColumnGroup(string Label, string FullLabel, IReadOnlyList<int> Values);

/// <summary>
/// Sunucuda SVG olarak çizilen sütun grafiği. Yatay konumlar yüzde, dikey konumlar piksel olarak hesaplanır;
/// böylece grafik genişliğe uyar ama yazılar ölçeklenmez.
/// </summary>
public sealed record ColumnChart(
    string Id,
    string Title,
    string CategoryHeader,
    IReadOnlyList<ChartSeries> Series,
    IReadOnlyList<ColumnGroup> Groups,
    bool ThinLabels = false)
{
    public const int Height = 200;
    public const int PlotTop = 12;
    public const int Baseline = 172;
    public const int LabelY = 192;
    public const int MaxBarWidth = 18;
    public const int SeriesGap = 2;

    public int Max => Groups.SelectMany(g => g.Values).DefaultIfEmpty(0).Max();

    public (int Top, int Step) Axis => ChartScale.Axis(Max);

    public IEnumerable<int> Ticks
    {
        get
        {
            var (top, step) = Axis;
            for (var v = 0; v <= top; v += step)
            {
                yield return v;
            }
        }
    }

    public double SlotPercent => 100.0 / Groups.Count;

    /// <summary>Tek bir sütunun genişliği; iki seride sütunlar 2 piksel boşlukla yan yana durur.</summary>
    public int BarWidth => Series.Count == 1 ? MaxBarWidth : (MaxBarWidth * 2 - SeriesGap * (Series.Count - 1)) / Series.Count;

    /// <summary>Sütun grubunun dilim ortasına göre sol kenarı (piksel).</summary>
    public int BarOffset(int seriesIndex)
    {
        var groupWidth = BarWidth * Series.Count + SeriesGap * (Series.Count - 1);
        return -groupWidth / 2 + seriesIndex * (BarWidth + SeriesGap);
    }

    public double Y(int value) => Baseline - BarHeight(value);

    public double BarHeight(int value) => ChartScale.Length(value, Axis.Top, Baseline - PlotTop);

    public static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

public static class ChartScale
{
    private static readonly int[] Multipliers = [1, 2, 5];

    /// <summary>
    /// Tamsayı sayımlar için "yuvarlak" eksen: en fazla 4 aralık, adımlar 1-2-5 dizisinden.
    /// Ör. 7 → (8, 2), 23 → (25, 5); boş grafikte 0 → (1, 1).
    /// </summary>
    public static (int Top, int Step) Axis(int max, int maxIntervals = 4)
    {
        if (max <= 0)
        {
            return (1, 1);
        }

        for (var magnitude = 1; ; magnitude *= 10)
        {
            foreach (var m in Multipliers)
            {
                var step = m * magnitude;
                var intervals = (max + step - 1) / step;
                if (intervals <= maxIntervals)
                {
                    return (intervals * step, step);
                }
            }
        }
    }

    /// <summary>Değerin eksen üst sınırına göre piksel uzunluğu (0.1 piksele yuvarlanır).</summary>
    public static double Length(int value, int top, int span) =>
        value <= 0 || top <= 0 ? 0 : Math.Round(Math.Min(value, top) * span / (double)top, 1);
}
