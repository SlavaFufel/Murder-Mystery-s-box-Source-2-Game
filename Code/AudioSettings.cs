using Sandbox;
using System;

/// <summary>
/// Клиент-локальные настройки громкости. Хранятся в FileSystem.Data/audio.txt.
/// MusicVolume управляет фоновой музыкой (HubMusic), SfxVolume — всеми звуковыми
/// эффектами, проигрываемыми через Sfx.Play, VoiceVolume — общей громкостью
/// голосов всех игроков (применяется в VoicePushToTalk к Voice.Volume).
/// </summary>
public static class AudioSettings
{
	private const string FileName = "audio.txt";

	private static float _music = 0.2f;
	private static float _sfx = 0.5f;
	private static float _voice = 1.0f;
	private static bool _loaded = false;

	public static int Version { get; private set; } = 0;

	// "Сырое" значение ползунка 0..1 (то что видит пользователь).
	public static float MusicVolume
	{
		get { EnsureLoaded(); return _music; }
		set { Set( ref _music, Math.Clamp( value, 0f, 1f ) ); }
	}

	public static float SfxVolume
	{
		get { EnsureLoaded(); return _sfx; }
		set { Set( ref _sfx, Math.Clamp( value, 0f, 1f ) ); }
	}

	public static float VoiceVolume
	{
		get { EnsureLoaded(); return _voice; }
		set { Set( ref _voice, Math.Clamp( value, 0f, 2f ) ); }
	}

	// Реальная громкость с аудио-кривой (восприятие громкости логарифмическое:
	// линейный множитель 0.1 звучит как ~32% от максимума, а не 10%).
	// Возводим в степень 3 — мягкий «студийный» curve: 50% по ползунку ≈ 12% по громкости,
	// 10% по ползунку ≈ 0.1% по громкости.
	public static float EffectiveMusicVolume => Curve( MusicVolume );
	public static float EffectiveSfxVolume => Curve( SfxVolume );
	// Voice — без curve, ползунок 0..2 (можно усилить тихих игроков).
	// 1.0 = «как в игре», 2.0 = ×2 громче. Curve тут только мешала бы.
	public static float EffectiveVoiceVolume => VoiceVolume;

	private static float Curve( float v )
	{
		if ( v <= 0.001f ) return 0f;
		return v * v * v;
	}

	private static void Set( ref float field, float v )
	{
		EnsureLoaded();
		if ( Math.Abs( field - v ) < 0.0001f ) return;
		field = v;
		Version++;
		Save();
	}

	private static void Save()
	{
		try { FileSystem.Data.WriteAllText( FileName, $"{_music:0.###};{_sfx:0.###};{_voice:0.###}" ); }
		catch ( Exception e ) { Log.Warning( $"[AudioSettings] save failed: {e.Message}" ); }
	}

	private static void EnsureLoaded()
	{
		if ( _loaded ) return;
		_loaded = true;
		try
		{
			if ( !FileSystem.Data.FileExists( FileName ) ) return;
			var raw = FileSystem.Data.ReadAllText( FileName )?.Trim();
			if ( string.IsNullOrEmpty( raw ) ) return;
			var parts = raw.Split( ';' );
			if ( parts.Length >= 1 && float.TryParse( parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m ) )
				_music = Math.Clamp( m, 0f, 1f );
			if ( parts.Length >= 2 && float.TryParse( parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s ) )
				_sfx = Math.Clamp( s, 0f, 1f );
			if ( parts.Length >= 3 && float.TryParse( parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v ) )
				_voice = Math.Clamp( v, 0f, 2f );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[AudioSettings] load failed: {e.Message}" );
		}
	}
}

/// <summary>
/// Обёртка над Sound.Play, которая применяет AudioSettings.SfxVolume.
/// </summary>
public static class Sfx
{
	public static SoundHandle Play( string sound )
	{
		return Play( sound, 1f );
	}

	// 2D-перегрузка с множителем громкости (как у позиционной). Нужна
	// чтобы тонкие интерфейсные звуки (тик прокрутки кейса, клик) можно
	// было сделать тише общего SFX-канала, не правя сам .sound-ассет.
	public static SoundHandle Play( string sound, float multiplier )
	{
		if ( string.IsNullOrEmpty( sound ) ) return null;
		var h = Sound.Play( sound );
		if ( h != null ) h.Volume = AudioSettings.EffectiveSfxVolume * multiplier;
		return h;
	}

	public static SoundHandle Play( string sound, Vector3 pos )
	{
		if ( string.IsNullOrEmpty( sound ) ) return null;
		var h = Sound.Play( sound, pos );
		if ( h != null ) h.Volume = AudioSettings.EffectiveSfxVolume;
		return h;
	}

	// Перегрузка с множителем громкости — для звуков, которые хочется
	// сделать заметнее на фоне общего SFX-канала (напр. взмах ножа).
	public static SoundHandle Play( string sound, Vector3 pos, float multiplier )
	{
		if ( string.IsNullOrEmpty( sound ) ) return null;
		var h = Sound.Play( sound, pos );
		if ( h != null ) h.Volume = AudioSettings.EffectiveSfxVolume * multiplier;
		return h;
	}
}
