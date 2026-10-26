namespace Elbwald.DesktopTools.Audio.Editor.Basic.Models;

/// <summary>
/// Compact spectrogram matrix. Column 0 is the beginning of the analyzed time window;
/// rows are logarithmically spaced from MinFrequencyHz to MaxFrequencyHz.
/// Values are dBFS and are intentionally analysis-only project derivatives.
/// </summary>
public sealed class SpectrogramData
{
    public SpectrogramData(
        int columns,
        int rows,
        int sampleRate,
        int fftSize,
        double startSeconds,
        double durationSeconds,
        double minFrequencyHz,
        double maxFrequencyHz,
        float[] magnitudesDb)
    {
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (fftSize <= 0) throw new ArgumentOutOfRangeException(nameof(fftSize));
        if (magnitudesDb.Length != checked(columns * rows))
        {
            throw new ArgumentException("Die Spektrogrammdaten passen nicht zur Matrixgröße.", nameof(magnitudesDb));
        }

        Columns = columns;
        Rows = rows;
        SampleRate = sampleRate;
        FftSize = fftSize;
        StartSeconds = Math.Max(0, startSeconds);
        DurationSeconds = Math.Max(0, durationSeconds);
        MinFrequencyHz = Math.Max(1, minFrequencyHz);
        MaxFrequencyHz = Math.Max(MinFrequencyHz, maxFrequencyHz);
        MagnitudesDb = magnitudesDb;
    }

    public int Columns { get; }
    public int Rows { get; }
    public int SampleRate { get; }
    public int FftSize { get; }
    public double StartSeconds { get; }
    public double DurationSeconds { get; }
    public double MinFrequencyHz { get; }
    public double MaxFrequencyHz { get; }
    public float[] MagnitudesDb { get; }

    public float this[int column, int row] => MagnitudesDb[column * Rows + row];
}

/// <summary>
/// Learned stationary-noise spectrum. It is not yet a processing mask; patch 0013 only
/// learns and visualizes it so the user can verify that the selected region really is noise.
/// </summary>
public sealed class NoiseProfileData
{
    public NoiseProfileData(
        int sampleRate,
        int fftSize,
        double selectionStartSeconds,
        double selectionDurationSeconds,
        double[] magnitudesDb)
    {
        SampleRate = sampleRate;
        FftSize = fftSize;
        SelectionStartSeconds = Math.Max(0, selectionStartSeconds);
        SelectionDurationSeconds = Math.Max(0, selectionDurationSeconds);
        MagnitudesDb = magnitudesDb ?? throw new ArgumentNullException(nameof(magnitudesDb));
    }

    public int SampleRate { get; }
    public int FftSize { get; }
    public double SelectionStartSeconds { get; }
    public double SelectionDurationSeconds { get; }
    public double[] MagnitudesDb { get; }
    public bool HasData => SampleRate > 0 && FftSize > 0 && MagnitudesDb.Length > 0;
}
