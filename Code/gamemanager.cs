using Sandbox;
using System.Linq;

/// <summary>
/// Хаб-координатор. Сама game-логика (state machine, роли, монеты, win-condition)
/// теперь живёт в GameRoom. GameManager только:
///   • Применяет общие правила к зашедшим игрокам (late-join → spectator, cap)
///   • Хранит список комнат для UI / quick play
///   • Поддерживает совместимость со старыми ссылками на Instance/CurrentState
///     (для UI которые читают «состояние игры» — теперь они должны
///     читать у GameRoom игрока)
/// </summary>
public sealed class GameManager : Component
{
	public enum GameState { WaitingForPlayers, StartingRound, Playing, RoundEnd }

	[Property] public int GlobalMaxPlayers { get; set; } = 64;

	// Куда телепортировать игрока когда он возвращается в хаб (например /hub команда).
	[Property] public GameObject HubSpawnPoint { get; set; }

	public static GameManager Instance { get; private set; }

	// Кэш «к какой руме принадлежит локальный игрок» — обновляется раз в кадр
	// в OnUpdate. Все per-frame OnUpdate'ы (PlayerStats, Coin, RagdollBody,
	// ItemVisibilityController и т.д.) дёргают эту статику чтобы понять, нужно
	// ли вообще что-то делать с этим объектом, или он принадлежит другой руме
	// и его можно полностью отключить (не анимировать, не симулировать физику).
	// System.Guid.Empty = «хаб / scope-агностичный объект» — всегда виден.
	public static System.Guid LocalRoomId { get; private set; } = System.Guid.Empty;

	// Кэш локального игрока — обновляется раз в кадр в OnUpdate. Заменяет
	// россыпь Scene.GetAllComponents<PlayerStats>().FirstOrDefault(!IsProxy)
	// в OnUpdate других компонентов и UI-getters (горячий путь × кадры × прокси).
	public static PlayerStats LocalPlayer { get; private set; }

	// Кэшированное состояние локальной румы — заменяет вызовы CurrentState
	// (которые делали 2× GetAllComponents). Читается каждый кадр в UI/Visuals.
	public static GameState LocalRoomState { get; private set; } = GameState.WaitingForPlayers;
	public static float LocalRoomTimer { get; private set; } = 0f;
	public static string LocalRoomWinner { get; private set; } = "";

	/// <summary>
	/// True если объект с указанным RoomId сейчас в области интереса локального
	/// клиента — т.е. он в той же руме, что и локальный игрок (включая хаб:
	/// Empty == Empty считаются «той же зоной»). Используется для отключения
	/// тяжёлой работы (анимация скелетов, физика регдоллов, idle-анимация
	/// монет) у объектов из чужих рум.
	/// </summary>
	public static bool IsInLocalScope( System.Guid roomId )
	{
		return roomId == LocalRoomId;
	}

	/// <summary>Возвращает игрока в хаб (CurrentRoomId = Empty + телепорт).</summary>
	public static void ReturnPlayerToHub( PlayerStats player )
	{
		if ( player == null || Instance == null ) return;
		// Полностью «оживляем» — ResetForNewRound сбросит IsDead, IsSpectator,
		// roles, оживит PlayerController и рендереры. Иначе после смерти + /hub
		// игрок остаётся призраком, потому что SpectatorController триггерится
		// от IsDead == true и не отпускает камеру.
		player.ResetForNewRound();
		player.SetCurrentRoom( System.Guid.Empty );
		var pos = Instance.HubSpawnPoint?.WorldPosition ?? Instance.WorldPosition;
		player.TeleportTo( pos );
	}

	// Эти три свойства раньше делали GetAllComponents на каждый GET — что
	// при per-frame обращениях из PlayerStats.UpdateWeaponVisuals превращалось
	// в десятки лукапов в кадр на каждого игрока. Теперь читаем из кэша,
	// который обновляется один раз в кадре в OnUpdate.
	public GameState CurrentState => LocalRoomState;
	public float StateTimer => LocalRoomTimer;
	public string WinnerTeam => LocalRoomWinner;

	private readonly System.Collections.Generic.HashSet<System.Guid> _seenPlayers = new();

	protected override void OnAwake()
	{
		Instance = this;

		// Программно гарантируем что на GameManager-объекте есть MatchmakingQueue.
		// Раньше его требовалось добавлять руками в сцене — но JSON-правки сцены
		// не всегда подхватываются редактором, и на клиенте Instance оказывался
		// null → баннер «нужно ещё 5» висел вечно с дефолтами. Теперь компонент
		// присоединяется к тому же сетевому GameObject что и GameManager,
		// поэтому он есть на хосте и на всех клиентах автоматически.
		if ( GameObject.Components.Get<MatchmakingQueue>() == null )
		{
			GameObject.Components.Create<MatchmakingQueue>();
		}
	}

	protected override void OnUpdate()
	{
		// Один раз в кадр обновляем room-scope кэш — чтобы все остальные
		// per-frame OnUpdate'ы могли спросить «я в области интереса?» без
		// повторных GetAllComponents. Это работает на ВСЕХ клиентах (включая
		// не-хост), поэтому делаем ДО host-only ветки.
		var local = Scene.GetAllComponents<PlayerStats>().FirstOrDefault( x => !x.IsProxy );
		LocalPlayer = local;
		LocalRoomId = local?.CurrentRoomId ?? System.Guid.Empty;

		if ( LocalRoomId == System.Guid.Empty )
		{
			LocalRoomState = GameState.WaitingForPlayers;
			LocalRoomTimer = 0f;
			LocalRoomWinner = "";
		}
		else
		{
			GameRoom localRoom = null;
			foreach ( var r in Scene.GetAllComponents<GameRoom>() )
			{
				if ( r.GameObject.Id == LocalRoomId ) { localRoom = r; break; }
			}
			if ( localRoom != null )
			{
				LocalRoomState = (GameState)localRoom.StateInt;
				LocalRoomTimer = localRoom.StateTimer;
				LocalRoomWinner = localRoom.Winner ?? "";
			}
		}

		if ( !Networking.IsHost ) return;

		var allPlayers = Scene.GetAllComponents<PlayerStats>().ToList();
		ApplyLateJoinAndCap( allPlayers );
	}

	private void ApplyLateJoinAndCap( System.Collections.Generic.List<PlayerStats> allPlayers )
	{
		// Если игрок зашёл когда его комната уже играет — он spectator до конца раунда.
		foreach ( var p in allPlayers )
		{
			var id = p.GameObject.Id;
			if ( _seenPlayers.Contains( id ) ) continue;
			_seenPlayers.Add( id );

			var room = FindRoomForPlayer( p );
			if ( room != null && (room.State == GameRoom.GameState.Playing || room.State == GameRoom.GameState.StartingRound) )
				p.SetSpectator( true );
		}

		// Глобальный cap (на сервер целиком).
		int activeCount = allPlayers.Count( p => !p.IsSpectator );
		if ( activeCount > GlobalMaxPlayers )
		{
			foreach ( var p in allPlayers.Where( p => !p.IsSpectator ) )
			{
				if ( activeCount <= GlobalMaxPlayers ) break;
				p.SetSpectator( true );
				activeCount--;
			}
		}

		// Чистим список ушедших.
		var live = new System.Collections.Generic.HashSet<System.Guid>( allPlayers.Select( x => x.GameObject.Id ) );
		_seenPlayers.RemoveWhere( g => !live.Contains( g ) );
	}

	public static GameRoom FindRoomForPlayer( PlayerStats player )
	{
		if ( player == null || player.CurrentRoomId == System.Guid.Empty ) return null;
		return Game.ActiveScene.GetAllComponents<GameRoom>()
			.FirstOrDefault( r => r.GameObject.Id == player.CurrentRoomId );
	}
}
