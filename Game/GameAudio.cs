using Godot;

namespace ChromaDrop.Game;

public partial class GameAudio : Node
{
  private AudioStreamPlayer _lock = null!;
  private AudioStreamPlayer _match = null!;
  private AudioStreamPlayer _select = null!;
  public bool Enabled { get; set; } = true;

  public override void _Ready()
  {
    _lock = AddPlayer("res://Assets/Kenney/Audio/lock.ogg", -6);
    _match = AddPlayer("res://Assets/Kenney/Audio/match.ogg", -2);
    _select = AddPlayer("res://Assets/Kenney/Audio/select.ogg", -10);
  }

  public void Lock()
  {
    if (Enabled) _lock.Play();
  }

  public void Match(int chain)
  {
    if (!Enabled) return;
    _match.PitchScale = Math.Min(1.45f, 1 + (chain - 1) * 0.12f);
    _match.Play();
  }

  public void Select()
  {
    if (Enabled) _select.Play();
  }

  private AudioStreamPlayer AddPlayer(string path, float volumeDb)
  {
    var player = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), VolumeDb = volumeDb };
    AddChild(player);
    return player;
  }
}
