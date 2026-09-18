using Sandbox;
using System.Linq;

/// <summary>
/// Фоновый эмбиент (пение птиц) для основной игровой карты. Играет, когда
/// локальный игрок находится в любой GameRoom (CurrentRoomId != Empty).
/// Громкость управляется AudioSettings.MusicVolume (тот же слайдер, что и лобби-музыка).
/// </summary>
public sealed class GameAmbient : Component
{
	[Property] public string Sound { get; set; } = "sounds/music/game_ambient.sound";

	private SoundHandle _handle;

	protected override void OnUpdate()
	{
		var local = GameManager.LocalPlayer;
		bool inGame = local != null && local.CurrentRoomId != System.Guid.Empty;
		float vol = AudioSettings.EffectiveMusicVolume;
		bool shouldPlay = inGame && !string.IsNullOrEmpty( Sound ) && vol > 0.0001f;

		if ( shouldPlay )
		{
			if ( _handle == null || _handle.IsStopped )
				_handle = Sandbox.Sound.Play( Sound );
			if ( _handle != null )
				_handle.Volume = vol;
		}
		else if ( _handle != null && !_handle.IsStopped )
		{
			_handle.Stop();
			_handle = null;
		}
	}

	protected override void OnDisabled()
	{
		if ( _handle != null && !_handle.IsStopped )
			_handle.Stop();
		_handle = null;
	}
}
