using Sandbox;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Клиент-локальные per-player настройки голоса (множитель громкости 0..2 + mute-флаг).
/// Хранятся по SteamId в FileSystem.Data/voice_players.txt — у каждого игрока свои.
/// Используется в <see cref="VoicePushToTalk"/> для применения к Voice.Volume,
/// и в SettingsMenu Players-табе для редактирования.
/// </summary>
public static class VoicePersonalSettings
{
	private const string FileName = "voice_players.txt";

	// steamId → (multiplier 0..2, muted)
	private static readonly Dictionary<ulong, (float Mul, bool Muted)> _map = new();
	private static bool _loaded = false;

	public static int Version { get; private set; } = 0;

	public static float GetMultiplier( ulong steamId )
	{
		EnsureLoaded();
		return _map.TryGetValue( steamId, out var v ) ? v.Mul : 1f;
	}

	public static bool IsMuted( ulong steamId )
	{
		EnsureLoaded();
		return _map.TryGetValue( steamId, out var v ) && v.Muted;
	}

	public static void SetMultiplier( ulong steamId, float mul )
	{
		EnsureLoaded();
		mul = Math.Clamp( mul, 0f, 2f );
		var muted = _map.TryGetValue( steamId, out var v ) && v.Muted;
		// Удаляем дефолтные записи (mul == 1, !muted) — экономим место в файле.
		if ( Math.Abs( mul - 1f ) < 0.0001f && !muted ) _map.Remove( steamId );
		else _map[steamId] = (mul, muted);
		Version++;
		Save();
	}

	public static void SetMuted( ulong steamId, bool muted )
	{
		EnsureLoaded();
		var mul = _map.TryGetValue( steamId, out var v ) ? v.Mul : 1f;
		if ( Math.Abs( mul - 1f ) < 0.0001f && !muted ) _map.Remove( steamId );
		else _map[steamId] = (mul, muted);
		Version++;
		Save();
	}

	public static void ToggleMuted( ulong steamId ) => SetMuted( steamId, !IsMuted( steamId ) );

	/// <summary>Финальный множитель: mute → 0, иначе сохранённый mul (или 1).</summary>
	public static float EffectiveMultiplier( ulong steamId )
	{
		if ( steamId == 0 ) return 1f;
		EnsureLoaded();
		if ( !_map.TryGetValue( steamId, out var v ) ) return 1f;
		return v.Muted ? 0f : v.Mul;
	}

	private static void Save()
	{
		try
		{
			var sb = new StringBuilder();
			foreach ( var kv in _map )
			{
				sb.Append( kv.Key.ToString( CultureInfo.InvariantCulture ) );
				sb.Append( ':' );
				sb.Append( kv.Value.Mul.ToString( "0.###", CultureInfo.InvariantCulture ) );
				sb.Append( ':' );
				sb.Append( kv.Value.Muted ? '1' : '0' );
				sb.Append( '\n' );
			}
			FileSystem.Data.WriteAllText( FileName, sb.ToString() );
		}
		catch ( Exception e ) { Log.Warning( $"[VoicePersonalSettings] save failed: {e.Message}" ); }
	}

	private static void EnsureLoaded()
	{
		if ( _loaded ) return;
		_loaded = true;
		try
		{
			if ( !FileSystem.Data.FileExists( FileName ) ) return;
			var raw = FileSystem.Data.ReadAllText( FileName );
			if ( string.IsNullOrEmpty( raw ) ) return;
			foreach ( var line in raw.Split( '\n' ) )
			{
				var trimmed = line.Trim();
				if ( string.IsNullOrEmpty( trimmed ) ) continue;
				var parts = trimmed.Split( ':' );
				if ( parts.Length < 3 ) continue;
				if ( !ulong.TryParse( parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid ) ) continue;
				if ( !float.TryParse( parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mul ) ) mul = 1f;
				bool muted = parts[2] == "1";
				_map[sid] = (Math.Clamp( mul, 0f, 2f ), muted);
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[VoicePersonalSettings] load failed: {e.Message}" );
		}
	}
}
