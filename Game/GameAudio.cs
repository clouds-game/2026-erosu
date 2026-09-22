using Godot;

namespace ChromaDrop.Game;

public partial class GameAudio : AudioStreamPlayer
{
  public bool Enabled { get; set; }

  public void Tone(float frequency, float duration = 0.1f)
  {
    if (!Enabled) return;
    const int rate = 22050;
    var count = (int)(rate * duration);
    var data = new byte[count * 2];
    for (var i = 0; i < count; i++)
    {
      var envelope = Math.Min(1, i / 110.0) * Math.Pow(1 - (double)i / count, 2);
      var sample = (short)(Math.Sin(Math.Tau * frequency * i / rate) * envelope * 5000);
      data[i * 2] = (byte)(sample & 0xff);
      data[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
    }
    Stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = data };
    Play();
  }
}
