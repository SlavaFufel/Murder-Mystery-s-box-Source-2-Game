using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Per-room матчмейкинг: каждый игрок в очереди привязан к конкретной комнате
/// (выбранной кнопкой «Войти» или авто-выбранной через Quick Play). Хост
/// каждые ~1 сек смотрит каждую комнату: если ActivePlayerCount + кол-во
/// именно ЭТОЙ комнаты в очереди ≥ room.MinPlayers — телепортирует их.
///
/// MinPlayers/MaxPlayers тянутся напрямую из инспектора каждой комнаты.
/// Создаётся программно в GameManager.OnAwake — отдельной правки сцены
/// не требует.
/// </summary>
public sealed class MatchmakingQueue : Component
{
	public static MatchmakingQueue Instance { get; private set; }

	// Локальный клиент: в какой руме мы стоим в очереди (Empty = вне очереди).
	// Не [Sync] — у каждого клиента своё.
	public static System.Guid LocalPreferredRoom { get; set; } = System.Guid.Empty;
	public static bool LocalIsSearching => LocalPreferredRoom != System.Guid.Empty;

	// Хост-сторона: connection → к какой руме он привязан (preference).
	private readonly Dictionary<Connection, System.Guid> _preferences = new();
	private float _checkTimer = 0f;

	protected override void OnAwake()
	{
		if ( Instance != null && Instance != this ) return;
		Instance = this;
		LocalPreferredRoom = System.Guid.Empty;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;

		// 1. Чистим отключившихся и тех кто уже зашёл в комнату. ВАЖНО: игрок в
		// тир-зоне (CurrentRoomId == TirZone.TirRoomId) — НЕ в комнате, очередь
		// сохраняется. Когда тир оффлайн (TirZone.TirRoomId == Empty) — это условие
		// просто не сработает, поведение как раньше.
		var tirId = TirZone.TirRoomId;
		var activeConns = Scene.GetAllComponents<PlayerStats>()
			.Select( p => p.Network?.Owner )
			.Where( c => c != null )
			.ToHashSet();
		var toRemove = new List<Connection>();
		foreach ( var kv in _preferences )
		{
			if ( !activeConns.Contains( kv.Key ) ) { toRemove.Add( kv.Key ); continue; }
			var p = FindPlayerByConn( kv.Key );
			if ( p == null ) continue;
			bool inRoom = p.CurrentRoomId != System.Guid.Empty
			           && p.CurrentRoomId != tirId;
			if ( inRoom ) toRemove.Add( kv.Key );
		}
		foreach ( var c in toRemove ) _preferences.Remove( c );

		// 2. Обновляем per-room MatchmakingQueueCount.
		var rooms = GameRoom.All.ToList();
		foreach ( var r in rooms )
		{
			int cnt = _preferences.Values.Count( g => g == r.GameObject.Id );
			if ( r.MatchmakingQueueCount != cnt ) r.MatchmakingQueueCount = cnt;
		}

		// 3. Раз в секунду пробуем сматчить.
		_checkTimer -= Time.Delta;
		if ( _checkTimer <= 0f )
		{
			_checkTimer = 1f;
			TryAutoMatchAll( rooms );
		}
	}

	/// <summary>Клиент: встать в очередь на конкретную руму.</summary>
	public static void StartSearching( System.Guid roomId )
	{
		if ( roomId == System.Guid.Empty ) return;
		LocalPreferredRoom = roomId;
		Instance?.EnqueueSelf( roomId );
	}

	/// <summary>Клиент: Quick Play — выбираем лучшую руму автоматически.</summary>
	public static void StartQuickPlay()
	{
		var best = PickQuickPlayRoom();
		if ( best == null ) return;
		StartSearching( best.GameObject.Id );
	}

	/// <summary>Клиент: выйти из очереди.</summary>
	public static void StopSearching()
	{
		if ( LocalPreferredRoom == System.Guid.Empty ) return;
		LocalPreferredRoom = System.Guid.Empty;
		Instance?.DequeueSelf();
	}

	/// <summary>Лучшая рума для Quick Play — ближе всех к старту.</summary>
	public static GameRoom PickQuickPlayRoom()
	{
		return GameRoom.All
			.Where( r => r.State == GameRoom.GameState.WaitingForPlayers
			          && r.ActivePlayerCount < r.MaxPlayers )
			.OrderByDescending( r => r.ActivePlayerCount + r.MatchmakingQueueCount )
			.ThenBy( r => r.MinPlayers )
			.FirstOrDefault();
	}

	[Rpc.Broadcast]
	public void EnqueueSelf( System.Guid roomId )
	{
		if ( !Networking.IsHost ) return;
		var caller = Rpc.Caller;
		if ( caller == null ) return;
		if ( roomId == System.Guid.Empty ) return;

		_preferences[caller] = roomId;
		TryAutoMatchAll( GameRoom.All.ToList() );
	}

	[Rpc.Broadcast]
	public void DequeueSelf()
	{
		if ( !Networking.IsHost ) return;
		var caller = Rpc.Caller;
		if ( caller == null ) return;
		_preferences.Remove( caller );
	}

	private void TryAutoMatchAll( List<GameRoom> rooms )
	{
		foreach ( var room in rooms )
		{
			if ( room.State != GameRoom.GameState.WaitingForPlayers ) continue;

			int slots = room.MaxPlayers - room.ActivePlayerCount;
			if ( slots <= 0 ) continue;

			// Кто стоит в очереди именно этой комнаты.
			var candidates = _preferences
				.Where( kv => kv.Value == room.GameObject.Id )
				.Select( kv => kv.Key )
				.ToList();

			int total = room.ActivePlayerCount + candidates.Count;
			if ( total < room.MinPlayers ) continue;

			// Берём столько кандидатов сколько влезет в свободные слоты.
			// Игрок может быть в хабе ИЛИ в тире — оба сценария допустимы,
			// RequestRoomEntry телепортирует его в lobbyPoint новой румы.
			var tirId = TirZone.TirRoomId;
			var toMove = candidates.Take( slots ).ToList();
			foreach ( var conn in toMove )
			{
				_preferences.Remove( conn );
				var player = FindPlayerByConn( conn );
				if ( player == null ) continue;
				bool inHubOrTir = player.CurrentRoomId == System.Guid.Empty
				               || player.CurrentRoomId == tirId;
				if ( !inHubOrTir ) continue;
				player.RequestRoomEntry( room.GameObject.Id, false );
			}
		}
	}

	private PlayerStats FindPlayerByConn( Connection conn )
	{
		if ( conn == null ) return null;
		return Scene.GetAllComponents<PlayerStats>()
			.FirstOrDefault( p => p.Network?.Owner == conn );
	}
}
