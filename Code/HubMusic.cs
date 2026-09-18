using Sandbox;
using System.Linq;

public sealed class HubMusic : Component
{
	[Property] public string Music { get; set; } = "sounds/music/lobby.sound";

	private SoundHandle _handle;

	protected override void OnUpdate()
	{
		var local = GameManager.LocalPlayer;
		bool inHub = local != null && local.CurrentRoomId == System.Guid.Empty;
		float vol = AudioSettings.EffectiveMusicVolume;
		bool shouldPlay = inHub && !string.IsNullOrEmpty( Music ) && vol > 0.0001f;

		if ( shouldPlay )
		{
			if ( _handle == null || _handle.IsStopped )
				_handle = Sound.Play( Music );
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
