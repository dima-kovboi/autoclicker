using System.IO;
using System.Media;
using System.Windows;

namespace AutoClicker.Services;

/// <summary>
/// Сервис для воспроизведения звуковых эффектов.
/// Генерирует простые WAV-звуки программно (без внешних файлов).
/// </summary>
public static class SoundService
{
    private static bool _enabled = true;

    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>Звук запуска кликера (восходящий тон).</summary>
    public static void PlayStart()
    {
        if (!_enabled) return;
        PlayTone(880, 120, 0.3);
        Task.Run(async () => { await Task.Delay(80); PlayTone(1175, 100, 0.25); });
    }

    /// <summary>Звук остановки кликера (нисходящий тон).</summary>
    public static void PlayStop()
    {
        if (!_enabled) return;
        PlayTone(1175, 100, 0.25);
        Task.Run(async () => { await Task.Delay(80); PlayTone(660, 140, 0.2); });
    }

    /// <summary>Короткий клик (для переключений).</summary>
    public static void PlayClick()
    {
        if (!_enabled) return;
        PlayTone(1000, 50, 0.15);
    }

    /// <summary>Звук назначения горячей клавиши.</summary>
    public static void PlayBind()
    {
        if (!_enabled) return;
        PlayTone(660, 80, 0.2);
        Task.Run(async () => { await Task.Delay(60); PlayTone(880, 80, 0.2); });
    }

    /// <summary>Генерирует и воспроизводит однотонный WAV звук.</summary>
    private static void PlayTone(int frequency, int durationMs, double volume)
    {
        try
        {
            int sampleRate = 44100;
            int sampleCount = sampleRate * durationMs / 1000;
            byte[] wav = GenerateWav(frequency, sampleRate, sampleCount, volume);

            var stream = new MemoryStream(wav);
            var player = new SoundPlayer(stream);
            player.Play();
        }
        catch
        {
            // Тихо игнорируем ошибки звука
        }
    }

    /// <summary>Генерирует простой WAV файл с синусоидальным тоном.</summary>
    private static byte[] GenerateWave(int frequency, int sampleRate, int sampleCount, double volume)
    {
        byte[] data = new byte[sampleCount * 2]; // 16-bit mono

        for (int i = 0; i < sampleCount; i++)
        {
            double t = (double)i / sampleRate;
            double envelope = 1.0 - (double)i / sampleCount; // затухание
            double sample = Math.Sin(2 * Math.PI * frequency * t) * volume * envelope;
            short pcm = (short)(sample * short.MaxValue);
            data[i * 2] = (byte)(pcm & 0xFF);
            data[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        // WAV заголовок
        int dataSize = data.Length;
        int fileSize = 36 + dataSize;
        byte[] wav = new byte[44 + dataSize];

        // RIFF header
        wav[0] = (byte)'R'; wav[1] = (byte)'I'; wav[2] = (byte)'F'; wav[3] = (byte)'F';
        WriteInt(wav, 4, fileSize);
        wav[8] = (byte)'W'; wav[9] = (byte)'A'; wav[10] = (byte)'V'; wav[11] = (byte)'E';

        // fmt chunk
        wav[12] = (byte)'f'; wav[13] = (byte)'m'; wav[14] = (byte)'t'; wav[15] = (byte)' ';
        WriteInt(wav, 16, 16); // chunk size
        WriteShort(wav, 20, 1); // PCM format
        WriteShort(wav, 22, 1); // mono
        WriteInt(wav, 24, sampleRate);
        WriteInt(wav, 28, sampleRate * 2); // byte rate
        WriteShort(wav, 32, 2); // block align
        WriteShort(wav, 34, 16); // bits per sample

        // data chunk
        wav[36] = (byte)'d'; wav[37] = (byte)'a'; wav[38] = (byte)'t'; wav[39] = (byte)'a';
        WriteInt(wav, 40, dataSize);
        Array.Copy(data, 0, wav, 44, dataSize);

        return wav;
    }

    private static byte[] GenerateWav(int frequency, int sampleRate, int sampleCount, double volume)
        => GenerateWave(frequency, sampleRate, sampleCount, volume);

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteShort(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}
