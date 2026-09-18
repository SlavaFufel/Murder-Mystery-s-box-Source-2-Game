using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Глобальный лидерборд через Sandbox.Services. Тянет TOP-N по каждой статистике
/// (wins / kills / survived / detective / catches), включая офлайн-игроков —
/// данные хранятся в облаке Steam и доступны даже если игрок не на сервере.
///
/// Использование:
///   var top = LeaderboardCache.Get( "wins" );  // возвращает кешированный список,
///                                              // фоном дёргает Refresh раз в 30с
///   foreach ( var e in top ) Log.Info( $"{e.Rank}. {e.Name} = {e.Value}" );
///
/// Если Sandbox.Services API недоступен/упал — кеш остаётся пустым,
/// HubLeaderboardUI должен fallback'нуться на онлайн-игроков.
/// </summary>
public static class LeaderboardCache
{
	public class Entry
	{
		public int    Rank;
		public string Name;
		public int    Value;
		public long   SteamId;
	}

	// Ключ кеша = stat name без префикса (например "wins").
	// При запросе в Sandbox.Services добавляем префикс "mm_".
	private static readonly Dictionary<string, List<Entry>> _cache    = new();
	private static readonly Dictionary<string, float>       _lastTick = new();
	private const float RefreshIntervalSec = 30f;
	private const int   MaxFetch = 25;

	/// <summary>
	/// Возвращает кешированный список TOP-N для данной стат-категории. На первый
	/// вызов вернёт пустой список и стартанёт фоновую асинхронную загрузку;
	/// результаты появятся через 1-2с (Steam-запрос). Дальше обновляется раз в
	/// RefreshIntervalSec секунд автоматически.
	/// </summary>
	public static List<Entry> Get( string statName )
	{
		if ( string.IsNullOrEmpty( statName ) ) return new List<Entry>();

		if ( !_cache.TryGetValue( statName, out var list ) )
		{
			list = new List<Entry>();
			_cache[statName] = list;
		}

		float now = RealTime.Now;
		bool stale = !_lastTick.TryGetValue( statName, out var last )
			|| (now - last) > RefreshIntervalSec;
		if ( stale )
		{
			_lastTick[statName] = now;
			_ = RefreshAsync( statName );  // fire-and-forget
		}
		return list;
	}

	/// <summary>Принудительно дёрнуть обновление (например при открытии хаба).</summary>
	public static void ForceRefresh( string statName )
	{
		if ( string.IsNullOrEmpty( statName ) ) return;
		_lastTick[statName] = RealTime.Now;
		_ = RefreshAsync( statName );
	}

	private static async Task RefreshAsync( string statName )
	{
		try
		{
			// Точная сигнатура Sandbox.Services API чуть варьируется между версиями
			// s&box; этот блок использует наиболее распространённый вариант.
			// Если у тебя API другой — логируем warning и оставляем кеш пустым.
			var board = Sandbox.Services.Leaderboards.GetFromStat( "mm_" + statName );
			if ( board == null ) return;

			board.MaxEntries = MaxFetch;
			await board.Refresh();

			var fresh = new List<Entry>();
			int rank = 1;
			foreach ( var e in board.Entries )
			{
				fresh.Add( new Entry
				{
					Rank    = rank++,
					Name    = e.DisplayName ?? "Player",
					Value   = (int)e.Value,
					SteamId = (long)e.SteamId,
				} );
			}

			// Атомарно подменяем содержимое кеша.
			var existing = _cache[statName];
			existing.Clear();
			existing.AddRange( fresh );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Leaderboard] refresh '{statName}' failed: {ex.Message}" );
		}
	}
}
