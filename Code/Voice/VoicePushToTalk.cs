using Sandbox;

/// <summary>
/// Сетевой индикатор «говорит» + апдейтер громкости голоса.
/// Сам захват звука и передачу делает встроенный <c>Voice</c> компонент s&amp;box
/// (в редакторе он называется «Voice Transmitter») — настройки PushToTalk/OpenMic/InputAction
/// делаются прямо в его инспекторе.
///
/// На стороне владельца этот компонент только зеркалит <c>Transmitter.IsRecording</c>
/// в [Sync] поле <see cref="IsSpeaking"/>, чтобы UI у других игроков (нейм-плейты,
/// чат-индикатор) мог показать иконку 🎙 над говорящим.
///
/// На стороне «слушателя» (proxy) — каждый кадр пересчитывает <c>Transmitter.Volume</c>
/// из локальных настроек:
///   • <see cref="AudioSettings.EffectiveVoiceVolume"/> — общий слайдер в Settings → Audio
///   • <see cref="VoicePersonalSettings"/> — персональный множитель / mute по SteamId
///   • Room scope — игроков из других <see cref="GameRoom"/> заглушаем в 0.
/// </summary>
public sealed class VoicePushToTalk : Component
{
	// Анти-дрожь: флаг IsSpeaking держится ещё это время после остановки
	// записи — чтобы UI-иконка не моргала на коротких паузах в речи.
	[Property] public float HoldAfterRelease { get; set; } = 0.20f;

	// Voice (Voice Transmitter) на этом же GameObject. Если поле пустое —
	// автоматически найдём в OnStart.
	[Property] public Voice Transmitter { get; set; }

	[Sync] public bool IsSpeaking { get; private set; } = false;

	private float _speakingHoldUntil = 0f;
	private PlayerStats _stats;
	private float _nextVolumeRecalc = 0f;
	private CameraComponent _cachedCam;

	protected override void OnStart()
	{
		if ( Transmitter == null )
			Transmitter = Components.Get<Voice>( FindMode.EverythingInSelfAndDescendants );
		_stats = Components.Get<PlayerStats>( FindMode.EverythingInSelfAndAncestors );
	}

	protected override void OnUpdate()
	{
		if ( Transmitter == null ) return;

		// Прокси из чужой румы — гарантированно не слышен (vol=0). Пропускаем
		// весь блок: и зеркалить IsSpeaking тут нечего (мы прокси), и пересчёт
		// громкости даст ровно тот же 0.
		if ( IsProxy && _stats != null && !GameManager.IsInLocalScope( _stats.CurrentRoomId ) )
		{
			try { Transmitter.Volume = 0f; } catch { }
			return;
		}

		// Зеркалим IsSpeaking только у владельца — он один знает реальное состояние
		// своей записи; [Sync] разнесёт значение остальным.
		if ( !IsProxy )
		{
			bool recording = false;
			try { recording = Transmitter.IsRecording; } catch { }

			if ( recording )
			{
				IsSpeaking = true;
				_speakingHoldUntil = Time.Now + System.Math.Max( 0f, HoldAfterRelease );
			}
			else if ( IsSpeaking && Time.Now >= _speakingHoldUntil )
			{
				IsSpeaking = false;
			}
		}

		// Громкость воспроизведения чужого голоса считаем на ВСЕХ клиентах,
		// у которых это игрок-прокси (т.е. слушатель). У владельца локального
		// Transmitter Volume не влияет на собственное прослушивание — но и не
		// мешает, поэтому пусть тоже выставляется.
		// Троттлим до 10 Гц — слышимая разница в громкости плавно меняется,
		// per-frame пересчёт расстояния × все прокси = пустая трата.
		if ( Time.Now >= _nextVolumeRecalc )
		{
			_nextVolumeRecalc = Time.Now + 0.1f;
			ApplyVolume();
		}
	}

	// Максимальная дистанция (в units), за которой голос не слышен совсем.
	// ~600 units ≈ 8-10 шагов персонажа. Меняй здесь если нужно.
	private const float VoiceMaxDistance = 600f;

	private void ApplyVolume()
	{
		if ( Transmitter == null ) return;

		// Своего собственного Voice не трогаем — он не воспроизводится локально,
		// но может использоваться внутренне для уровня микрофона/мониторинга.
		if ( !IsProxy )
		{
			try { Transmitter.Volume = 1f; } catch { }
			return;
		}

		float vol = AudioSettings.EffectiveVoiceVolume;

		// Room scope: игрок из чужой комнаты не слышен.
		if ( _stats != null && !GameManager.IsInLocalScope( _stats.CurrentRoomId ) )
			vol = 0f;

		// Spectator segregation (#13): живые игроки не слышат спектаторов/мёртвых.
		// Спектаторы слышат живых и друг друга (как в текстовом чате).
		if ( vol > 0f && _stats != null && (_stats.IsDead || _stats.IsSpectator) )
		{
			var localPlayer = GameManager.LocalPlayer;
			bool listenerIsAlive = localPlayer != null && !localPlayer.IsDead && !localPlayer.IsSpectator;
			if ( listenerIsAlive )
				vol = 0f;
		}

		// Distance falloff (#12): линейное затухание, полная тишина за VoiceMaxDistance.
		if ( vol > 0f && _stats != null )
		{
			if ( _cachedCam == null || !_cachedCam.IsValid ) _cachedCam = Scene?.Camera;
			var cam = _cachedCam;
			if ( cam != null )
			{
				float dist = cam.WorldPosition.Distance( _stats.GameObject.WorldPosition );
				if ( dist >= VoiceMaxDistance )
					vol = 0f;
				else
					vol *= System.Math.Max( 0f, 1f - dist / VoiceMaxDistance );
			}
		}

		// Персональный множитель / mute по SteamId владельца этого префаба.
		if ( vol > 0f )
		{
			ulong sid = 0;
			try
			{
				var owner = Network.Owner;
				if ( owner != null ) sid = owner.SteamId;
			}
			catch { }
			if ( sid != 0 )
				vol *= VoicePersonalSettings.EffectiveMultiplier( sid );
		}

		try { Transmitter.Volume = vol; } catch { }
	}

	protected override void OnDisabled()
	{
		IsSpeaking = false;
	}
}
