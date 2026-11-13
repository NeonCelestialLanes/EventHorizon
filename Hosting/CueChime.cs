using System.Runtime.InteropServices;

namespace EventHorizon.Hosting;

internal static class CueChime
{
    private const uint Async = 0x0001;
    private const uint NoDefault = 0x0002;
    private const uint Memory = 0x0004;

    private static readonly byte[] Wave = Build();

    public static void Play()
    {
        try
        {
            _ = PlaySound(Wave, IntPtr.Zero, Async | NoDefault | Memory);
        }
        catch (Exception)
        {
            // A missing sound device should not stop the visual alert.
        }
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(byte[] sound, IntPtr module, uint flags);

    private static byte[] Build()
    {
        const int rate = 22050;
        var samples = new List<short>();
        Tone(samples, rate, 784, 0.14);
        Tone(samples, rate, 0, 0.04);
        Tone(samples, rate, 1174, 0.22);
        var data = samples.Count * 2;
        var bytes = new byte[44 + data];
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'F';
        bytes[3] = (byte)'F';
        Write(bytes, 4, 36 + data);
        bytes[8] = (byte)'W';
        bytes[9] = (byte)'A';
        bytes[10] = (byte)'V';
        bytes[11] = (byte)'E';
        bytes[12] = (byte)'f';
        bytes[13] = (byte)'m';
        bytes[14] = (byte)'t';
        bytes[15] = (byte)' ';
        Write(bytes, 16, 16);
        bytes[20] = 1;
        bytes[22] = 1;
        Write(bytes, 24, rate);
        Write(bytes, 28, rate * 2);
        bytes[32] = 2;
        bytes[34] = 16;
        bytes[36] = (byte)'d';
        bytes[37] = (byte)'a';
        bytes[38] = (byte)'t';
        bytes[39] = (byte)'a';
        Write(bytes, 40, data);
        for (var index = 0; index < samples.Count; index++)
        {
            bytes[44 + (index * 2)] = (byte)samples[index];
            bytes[45 + (index * 2)] = (byte)(samples[index] >> 8);
        }

        return bytes;
    }

    private static void Tone(List<short> samples, int rate, double hz, double seconds)
    {
        var count = Math.Max(1, (int)(rate * seconds));
        for (var index = 0; index < count; index++)
        {
            if (hz <= 0)
            {
                samples.Add(0);
                continue;
            }

            var fade = Math.Sin(Math.PI * index / count);
            var wave = Math.Sin(2 * Math.PI * hz * index / rate);
            samples.Add((short)(wave * fade * 12000));
        }
    }

    private static void Write(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}
