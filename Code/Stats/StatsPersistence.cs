using Sandbox;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Сохраняет/загружает per-player данные.
///
/// Двухуровневое хранилище:
///   1. Локальный файл (FileSystem.Data / stats.json) — быстрый кэш, мгновенно
///      доступен при старте. Пропадает при переустановке Windows.
///   2. Облако (Sandbox.Services.Kv) — привязан к Steam ID, выживает после
///      переустановки. Чуть медленнее (сеть). Является авторитетным источником.
///
/// Порядок работы:
///   OnStart → ApplyTo (локально, мгновенно) → SyncFromCloudAsync (облако, ~1-2 сек)
///   StoreFrom → Save (локально) + SaveToCloudAsync (облако, fire-and-forget)
/// </summary>
public static class StatsPersistence
{
	private const string FileName      = "stats.json";
	// Ключ в Kv: уникален в рамках пакета и Steam-аккаунта игрока.
	private const string KvKey         = "inv_v1";

	// ── StatEntry ────────────────────────────────────────────────────────────

	public class StatEntry
	{
		public int Wins      { get; set; }
		public int Kills     { get; set; }
		public int Survived  { get; set; }
		public int Detective { get; set; }
		public int Catches   { get; set; }

		// Валюта. У новых игроков 0 — балансировка через ежедневки/раунды.
		public int Crystals { get; set; } = 0;

		// Антиабуз: сколько кристаллов заработано за сегодня и дата ("yyyy-MM-dd").
		public int    DailyCrystalsEarned { get; set; } = 0;
		public string DailyCrystalsDate   { get; set; } = "";

		// Косметика.
		public Dictionary<string, List<string>> OwnedCosmetics  { get; set; } = new();
		public Dictionary<string, string>       EquippedCosmetics { get; set; } = new();

		// Колесо эмоций: 6 id'ов в фиксированных слотах. Если массив null или
		// короче 6 — клиент возьмёт дефолты из CosmeticCatalog.DefaultEmoteWheel.
		public string[] EmoteWheel { get; set; } = null;

		// Кейсы: id → количество.
		public Dictionary<string, int> OwnedCases { get; set; } = new();

		// Получил ли стартовый кейс.
		public bool HasReceivedStarterCase { get; set; } = false;

		// Использованные промокоды.
		public List<string> RedeemedPromos { get; set; } = new();

		// Ежедневные награды.
		public string LastDailyClaimDate { get; set; } = "";
		public int    DailyStreak        { get; set; } = 0;
		public string LastGamePlayedDate { get; set; } = "";

		// Туториал.
		public bool HasSeenTutorial { get; set; } = false;

		// Сколько уже пушнуто в Sandbox.Services.Stats (для выравнивания при рестарте).
		public int PushedWins      { get; set; } = 0;
		public int PushedKills     { get; set; } = 0;
		public int PushedSurvived  { get; set; } = 0;
		public int PushedDetective { get; set; } = 0;
		public int PushedCatches   { get; set; } = 0;

		// Тир: лучший серийный стрик (хитов подряд без промаха в окне 1.5с).
		// Глобальный — сохраняется между сессиями.
		public int TirBestStreak    { get; set; } = 0;
		public int PushedTirStreak  { get; set; } = 0;
	}

	private static Dictionary<string, StatEntry> _data;

	// ── Локальное хранилище ──────────────────────────────────────────────────

	public static void Load()
	{
		try
		{
			if ( FileSystem.Data.FileExists( FileName ) )
			{
				var json = FileSystem.Data.ReadAllText( FileName );
				_data = JsonSerializer.Deserialize<Dictionary<string, StatEntry>>( json )
					?? new Dictionary<string, StatEntry>();
			}
			else
			{
				_data = new Dictionary<string, StatEntry>();
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[StatsPersistence] load failed: {e.Message}" );
			_data = new Dictionary<string, StatEntry>();
		}
	}

	/// <summary>Сохраняет весь локальный файл. Быстро, синхронно.</summary>
	public static void Save()
	{
		if ( _data == null ) return;
		try
		{
			var json = JsonSerializer.Serialize( _data, new JsonSerializerOptions { WriteIndented = true } );
			FileSystem.Data.WriteAllText( FileName, json );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[StatsPersistence] local save failed: {e.Message}" );
		}
	}

	// ── Облачное хранилище ──────────────────────────────────────────────────
	// Backend: sboxcool Network Storage (Public API mode, per-steamid, single save).
	// Документ = один игрок, ключ = Steam ID (берётся из X-Sbox-Token sboxcool'ом).
	// Credentials грузятся autoconfig'ом из Assets/network-storage.credentials.json
	// (создаётся через Editor → Network Storage → Setup).
	//
	// ЭТАП 1: простая прямая запись/чтение. Без дебаунса, без timestamp'ов.
	// Если rate limit ~6/min будет реальной проблемой — добавим throttle через
	// OnFixedUpdate в PlayerStats (без Task.Delay-loop'ов).

	/// <summary>Id коллекции в sboxcool dashboard → Network Storage → murder-mystery → player_data.</summary>
	private const string CollectionId = "2f4ed44618634f40";

	/// <summary>Slug endpoint'а для загрузки сейва (GET, single Read step с Echo response).</summary>
	private const string LoadEndpointSlug = "load-player-data";

	/// <summary>Slug endpoint'а для сохранения сейва (POST, input `data`, Write step set field "save").</summary>
	private const string SaveEndpointSlug = "save-player-data";

	/// <summary>
	/// Базовый интервал между реальными POST'ами в облако.
	/// sboxcool лимитит коллекцию ~6 saves/min (~1 every 10s). Берём 12s
	/// с запасом — даём максимум 5/min, никогда не пробиваем лимит.
	/// К нему прибавляется случайный jitter ±3с — чтобы все игроки не
	/// отстреливали POST в один момент после общего события (раунд кончился).
	/// </summary>
	private const double CloudSaveThrottleSeconds = 12.0;
	private const double CloudSaveJitterSeconds = 3.0;

	// Pending-снапшот. SaveToCloudAsync только помечает его; реальный POST
	// уходит из TryFlushCloudSave (вызывается из PlayerStats.OnFixedUpdate)
	// либо из FlushCloudSaveAsync (при выходе игрока).
	private static StatEntry _pendingCloudEntry;
	private static DateTime _lastCloudSaveUtc = DateTime.MinValue;
	private static double _currentThrottleSeconds = CloudSaveThrottleSeconds;
	private static readonly object _cloudLock = new();
	private static readonly Random _jitterRng = new();

	/// <summary>
	/// Помечает entry для отложенного облачного сохранения. Реальный POST
	/// уйдёт из TryFlushCloudSaveAsync (раз в 12 секунд) или FlushCloudSaveAsync
	/// (при выходе). Возвращает сразу — не блокирует геймплей.
	/// </summary>
	public static Task SaveToCloudAsync( StatEntry entry )
	{
		if ( entry == null ) return Task.CompletedTask;
		lock ( _cloudLock )
		{
			_pendingCloudEntry = entry;
		}
		return Task.CompletedTask;
	}

	/// <summary>
	/// Если есть pending entry и с прошлого реального save прошло ≥ 12 секунд —
	/// делает POST. Вызывается из PlayerStats.OnFixedUpdate.
	/// </summary>
	public static void TryFlushCloudSave()
	{
		StatEntry toSave = null;
		lock ( _cloudLock )
		{
			if ( _pendingCloudEntry == null ) return;
			if ( (DateTime.UtcNow - _lastCloudSaveUtc).TotalSeconds < _currentThrottleSeconds ) return;
			toSave = _pendingCloudEntry;
			_pendingCloudEntry = null;
			_lastCloudSaveUtc = DateTime.UtcNow;
			// Перекатываем jitter — следующее окно будет смещено случайно на
			// ±CloudSaveJitterSeconds, чтобы при общих событиях (раунд кончился
			// у всех 64 игроков) POST'ы не выстраивались в одну миллисекунду.
			_currentThrottleSeconds = CloudSaveThrottleSeconds + (_jitterRng.NextDouble() * 2 - 1) * CloudSaveJitterSeconds;
		}
		_ = UploadEntryAsync( toSave );
	}

	/// <summary>
	/// Принудительно отправить pending entry прямо сейчас, игнорируя throttle.
	/// Использовать при выходе игрока (OnDestroy) — чтобы последние изменения
	/// за 12-секундное окно не потерялись.
	/// </summary>
	public static Task FlushCloudSaveAsync()
	{
		StatEntry toSave = null;
		lock ( _cloudLock )
		{
			if ( _pendingCloudEntry == null ) return Task.CompletedTask;
			toSave = _pendingCloudEntry;
			_pendingCloudEntry = null;
			_lastCloudSaveUtc = DateTime.UtcNow;
		}
		return UploadEntryAsync( toSave );
	}

	private static async Task UploadEntryAsync( StatEntry entry )
	{
		if ( entry == null ) return;

		// Autoconfig у sboxcool ленивый — срабатывает только в EnsureConfigured()
		// внутри Save/GetDocument. Дёргаем явно, иначе early-return ниже сработает
		// в первый вызов и save потеряется.
		if ( !NetworkStorage.IsConfigured ) NetworkStorage.AutoConfigure();
		if ( !NetworkStorage.IsConfigured )
		{
			Log.Warning( "[StatsPersistence] cloud save skipped: NetworkStorage not configured (Editor → Network Storage → Setup)" );
			return;
		}

		var steamId = Sandbox.Game.SteamId.ToString();
		if ( steamId == "0" || string.IsNullOrEmpty( steamId ) )
		{
			Log.Warning( "[StatsPersistence] cloud save skipped: Steam ID is 0/empty" );
			return;
		}

		try
		{
			var entryJson = JsonSerializer.Serialize( entry );
			var result = await NetworkStorage.CallEndpoint( SaveEndpointSlug, new { data = entryJson } );
			Log.Info( $"[StatsPersistence] cloud save → {(result.HasValue ? "OK" : "no-result")} (steamId={steamId}, {entryJson.Length} bytes)" );

			// Debug: что вернул save endpoint?
			if ( result.HasValue )
			{
				var rawResult = result.Value.GetRawText();
				Log.Info( $"[StatsPersistence] DEBUG cloud save response: {(rawResult.Length > 400 ? rawResult.Substring( 0, 400 ) + "..." : rawResult)}" );
				LogTopLevelKeys( result.Value, "save response" );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[StatsPersistence] cloud save failed: {ex.Message}" );
		}
	}

	/// <summary>
	/// Опции для cloud Deserialize: sboxcool отдаёт JSON в camelCase
	/// ("wins", "ownedCosmetics"), а наши свойства в StatEntry в PascalCase
	/// ("Wins", "OwnedCosmetics"). Без PropertyNameCaseInsensitive
	/// Deserialize молча игнорирует все поля и возвращает StatEntry с
	/// дефолтами — это уничтожило прогресс одному игроку, фикс критичный.
	/// </summary>
	private static readonly JsonSerializerOptions CloudJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	public static async Task<StatEntry> LoadFromCloudAsync()
	{
		if ( !NetworkStorage.IsConfigured ) NetworkStorage.AutoConfigure();
		if ( !NetworkStorage.IsConfigured )
		{
			Log.Warning( "[StatsPersistence] cloud load skipped: NetworkStorage not configured" );
			return null;
		}

		var steamId = Sandbox.Game.SteamId.ToString();
		if ( steamId == "0" || string.IsNullOrEmpty( steamId ) ) return null;

		// Первый запрос к sboxcool часто валится по таймауту из-за cold-start
		// (DNS/TLS handshake занимают ~12 сек). CallEndpoint проглатывает
		// exception и возвращает null. В наивной логике мы решим «документа нет,
		// нужно залить local наверх» — и тем самым **затрём реальные данные в
		// облаке**, если они там есть. Поэтому делаем один retry через 2 сек.
		for ( int attempt = 1; attempt <= 2; attempt++ )
		{
			try
			{
				// Передаём пустой input — это форсит POST в библиотеке CallEndpoint
				// (без input идёт GET, который sboxcool отдаёт 404 на endpoint'ах).
				var response = await NetworkStorage.CallEndpoint( LoadEndpointSlug, new { } );
				if ( !response.HasValue )
				{
					if ( attempt == 1 )
					{
						Log.Info( "[StatsPersistence] cloud load attempt 1 → no result, retrying in 2s..." );
						await Task.Delay( 2000 );
						continue;
					}
					Log.Info( $"[StatsPersistence] cloud load: no document yet (steamId={steamId})" );
					return null;
				}

				// Debug: что в ответе?
				var rawResponse = response.Value.GetRawText();
				Log.Info( $"[StatsPersistence] DEBUG cloud load response shape: {(rawResponse.Length > 500 ? rawResponse.Substring(0, 500) + "..." : rawResponse)}" );
				LogTopLevelKeys( response.Value, "load response" );

				if ( TryExtractStatEntry( response.Value, out var entry, out int rawSize ) )
				{
					Log.Info( $"[StatsPersistence] cloud load → {rawSize} bytes (steamId={steamId}, attempt={attempt})" );
					return entry;
				}

				Log.Info( $"[StatsPersistence] cloud load: response had no recognizable save (steamId={steamId})" );
				return null;
			}
			catch ( Exception ex )
			{
				Log.Warning( $"[StatsPersistence] cloud load attempt {attempt} failed: {ex.Message}" );
				if ( attempt == 1 ) await Task.Delay( 2000 );
			}
		}
		return null;
	}

	private static void LogTopLevelKeys( JsonElement el, string label )
	{
		if ( el.ValueKind != JsonValueKind.Object )
		{
			Log.Info( $"[StatsPersistence] DEBUG {label}: not an object, kind={el.ValueKind}" );
			return;
		}
		var keys = new System.Collections.Generic.List<string>();
		foreach ( var prop in el.EnumerateObject() )
			keys.Add( $"{prop.Name}={prop.Value.ValueKind}" );
		Log.Info( $"[StatsPersistence] DEBUG {label} top-level keys: [{string.Join( ", ", keys )}]" );
	}

	/// <summary>
	/// Пытается достать StatEntry из ответа endpoint'а load-player-data.
	/// Echo mode возвращает выход шага read_0. Документ имеет вид { save: "<JSON>" }
	/// (мы шлём StatEntry как JSON-строку, потому что у sboxcool нет универсального
	/// JSON-типа в input fields).
	/// </summary>
	private static bool TryExtractStatEntry( JsonElement el, out StatEntry entry, out int rawSize )
	{
		entry = null;
		rawSize = 0;
		if ( el.ValueKind != JsonValueKind.Object ) return false;

		// Вариант 1: { save: "<JSON string>" } — наш стандартный shape (string-wrapped).
		if ( el.TryGetProperty( "save", out var saveStr ) && saveStr.ValueKind == JsonValueKind.String )
			return TryDeserializeFromString( saveStr.GetString(), out entry, out rawSize );

		// Вариант 2: { save: <StatEntry object> } — старый shape если документ был
		// сохранён как объект (миграция / legacy).
		if ( el.TryGetProperty( "save", out var saveObj ) && saveObj.ValueKind == JsonValueKind.Object )
			return TryDeserialize( saveObj, out entry, out rawSize );

		// Вариант 3: { read_0: { save: "<JSON>" } } — Echo обернул в step id.
		if ( el.TryGetProperty( "read_0", out var read0 ) && read0.ValueKind == JsonValueKind.Object )
		{
			if ( read0.TryGetProperty( "save", out var s ) )
			{
				if ( s.ValueKind == JsonValueKind.String )
					return TryDeserializeFromString( s.GetString(), out entry, out rawSize );
				if ( s.ValueKind == JsonValueKind.Object )
					return TryDeserialize( s, out entry, out rawSize );
			}

			// Вариант 4: { read_0: <StatEntry flat> } — старый flat-документ.
			if ( LooksLikeStatEntry( read0 ) )
				return TryDeserialize( read0, out entry, out rawSize );
		}

		// Вариант 5: ответ — сам StatEntry (без обёрток).
		if ( LooksLikeStatEntry( el ) )
			return TryDeserialize( el, out entry, out rawSize );

		return false;
	}

	private static bool TryDeserialize( JsonElement el, out StatEntry entry, out int rawSize )
	{
		var raw = el.GetRawText();
		rawSize = raw?.Length ?? 0;
		try
		{
			entry = JsonSerializer.Deserialize<StatEntry>( raw, CloudJsonOptions );
			return entry != null;
		}
		catch
		{
			entry = null;
			return false;
		}
	}

	private static bool TryDeserializeFromString( string json, out StatEntry entry, out int rawSize )
	{
		entry = null;
		rawSize = json?.Length ?? 0;
		if ( string.IsNullOrEmpty( json ) ) return false;
		try
		{
			entry = JsonSerializer.Deserialize<StatEntry>( json, CloudJsonOptions );
			return entry != null;
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[StatsPersistence] deserialize save string failed: {ex.Message}" );
			return false;
		}
	}

	private static bool LooksLikeStatEntry( JsonElement el )
	{
		// Эвристика: смотрим есть ли хотя бы одно из «диагностичных» полей.
		// Если есть — это StatEntry (либо PascalCase либо camelCase).
		return el.TryGetProperty( "Wins", out _ )    || el.TryGetProperty( "wins", out _ )
			|| el.TryGetProperty( "Crystals", out _ ) || el.TryGetProperty( "crystals", out _ )
			|| el.TryGetProperty( "OwnedCosmetics", out _ ) || el.TryGetProperty( "ownedCosmetics", out _ );
	}

	/// <summary>
	/// Применяет облачные данные к PlayerStats и синхронизирует локальный кэш.
	/// Облако приоритетнее локального файла. Лидербордные статы берём максимумом
	/// (cloud vs local) — на случай если один из источников отстал.
	/// </summary>
	public static void ApplyCloudEntry( PlayerStats stats, StatEntry cloud, string steamId )
	{
		if ( stats == null || cloud == null || string.IsNullOrEmpty( steamId ) ) return;

		// Инвентарные данные — облако авторитетно.
		stats.Crystals = cloud.Crystals;
		stats.LoadCosmeticsFrom( cloud.OwnedCosmetics, cloud.EquippedCosmetics );
		stats.LoadCasesFrom( cloud.OwnedCases );
		stats.LoadPromosFrom( cloud.RedeemedPromos );
		stats.LoadDailyFrom( cloud.LastDailyClaimDate, cloud.DailyStreak, cloud.LastGamePlayedDate );

		// Если туториал был закрыт на другом устройстве — учитываем.
		if ( cloud.HasSeenTutorial )
			stats.TutorialVisible = false;

		// Лидербордные статы — максимум из обоих источников.
		var local = GetFor( steamId );
		stats.StatWins              = Math.Max( stats.StatWins,              cloud.Wins );
		stats.StatKills             = Math.Max( stats.StatKills,             cloud.Kills );
		stats.StatSurvived          = Math.Max( stats.StatSurvived,          cloud.Survived );
		stats.StatRoundsAsDetective = Math.Max( stats.StatRoundsAsDetective, cloud.Detective );
		stats.StatMurdererCatches   = Math.Max( stats.StatMurdererCatches,   cloud.Catches );
		stats.StatTirBestStreak     = Math.Max( stats.StatTirBestStreak,     cloud.TirBestStreak );

		// Обновляем локальный кэш данными из облака.
		local.Crystals              = cloud.Crystals;
		local.OwnedCosmetics        = cloud.OwnedCosmetics;
		local.EquippedCosmetics     = cloud.EquippedCosmetics;
		local.OwnedCases            = cloud.OwnedCases;
		local.RedeemedPromos        = cloud.RedeemedPromos;
		local.LastDailyClaimDate    = cloud.LastDailyClaimDate;
		local.DailyStreak           = cloud.DailyStreak;
		local.LastGamePlayedDate    = cloud.LastGamePlayedDate;
		local.HasSeenTutorial       = cloud.HasSeenTutorial;
		local.HasReceivedStarterCase = cloud.HasReceivedStarterCase;
		local.Wins                  = stats.StatWins;
		local.Kills                 = stats.StatKills;
		local.Survived              = stats.StatSurvived;
		local.Detective             = stats.StatRoundsAsDetective;
		local.Catches               = stats.StatMurdererCatches;
		local.TirBestStreak         = stats.StatTirBestStreak;

		Save(); // обновлённый локальный кэш
	}

	/// <summary>
	/// Триггерит облачное сохранение для конкретного игрока.
	/// Fire-and-forget. Для мест где используется GetFor + Save напрямую.
	/// </summary>
	public static void CloudSync( string steamId )
	{
		if ( string.IsNullOrEmpty( steamId ) || _data == null ) return;
		if ( _data.TryGetValue( steamId, out var entry ) )
			_ = SaveToCloudAsync( entry );
	}

	// ── Основные методы ApplyTo / StoreFrom ──────────────────────────────────

	public static StatEntry GetFor( string steamId )
	{
		if ( _data == null ) Load();
		if ( !_data.TryGetValue( steamId, out var e ) )
		{
			e = new StatEntry();
			_data[steamId] = e;
		}
		return e;
	}

	public static void ApplyTo( PlayerStats stats, string steamId )
	{
		if ( stats == null || string.IsNullOrEmpty( steamId ) ) return;
		var e = GetFor( steamId );
		stats.StatWins                = e.Wins;
		stats.StatKills               = e.Kills;
		stats.StatSurvived            = e.Survived;
		stats.StatRoundsAsDetective   = e.Detective;
		stats.StatMurdererCatches     = e.Catches;
		stats.StatTirBestStreak       = e.TirBestStreak;
		stats.Crystals                = e.Crystals;
		stats.LoadCosmeticsFrom( e.OwnedCosmetics, e.EquippedCosmetics );
		stats.LoadCasesFrom( e.OwnedCases );
		stats.LoadPromosFrom( e.RedeemedPromos );
		stats.LoadDailyFrom( e.LastDailyClaimDate, e.DailyStreak, e.LastGamePlayedDate );
		stats.LoadEmoteWheelFrom( e.EmoteWheel );
	}

	/// <summary>
	/// Сохраняет данные PlayerStats в StatEntry, пишет локально И в облако.
	/// </summary>
	public static void StoreFrom( PlayerStats stats, string steamId )
	{
		if ( stats == null || string.IsNullOrEmpty( steamId ) ) return;
		var e = GetFor( steamId );
		e.Wins        = stats.StatWins;
		e.Kills       = stats.StatKills;
		e.Survived    = stats.StatSurvived;
		e.Detective   = stats.StatRoundsAsDetective;
		e.Catches     = stats.StatMurdererCatches;
		e.TirBestStreak = stats.StatTirBestStreak;
		e.Crystals    = stats.Crystals;
		stats.SaveCosmeticsInto( e.OwnedCosmetics, e.EquippedCosmetics );
		stats.SaveCasesInto( e.OwnedCases );
		stats.SavePromosInto( e.RedeemedPromos );
		e.EmoteWheel         = stats.SaveEmoteWheelInto();
		e.LastDailyClaimDate = stats.LastDailyClaimDate;
		e.DailyStreak        = stats.DailyClaimStreak;
		e.LastGamePlayedDate = stats.LastGamePlayedDate;
		Save();                       // локально — мгновенно
		_ = SaveToCloudAsync( e );    // облако — fire-and-forget
	}
}
