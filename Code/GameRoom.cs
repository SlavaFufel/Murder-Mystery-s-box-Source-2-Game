using Sandbox;
using System.Collections.Generic;
using System.Linq;

public sealed class GameRoom : Component
{
	public enum GameState { WaitingForPlayers, StartingRound, Playing, RoundEnd }

	[Property] public string RoomName { get; set; } = "Игра 1";
	[Property] public int MinPlayers { get; set; } = 6;
	[Property] public int MaxPlayers { get; set; } = 16;
	[Property] public float StartCountdown { get; set; } = 30f;
	[Property] public float StartingPhaseTime { get; set; } = 5f;
	[Property] public float PlayingPhaseTime { get; set; } = 300f;
	[Property] public float RoundEndPhaseTime { get; set; } = 10f;

	// Монеты — ставишь точки руками в редакторе, они появляются рандомно.
	[Property] public GameObject CoinPrefab { get; set; }
	[Property, Group("Coins")] public List<GameObject> CoinSpawnPoints { get; set; } = new();
	[Property, Group("Coins")] public int InitialCoins { get; set; } = 3;
	// «Пульсирующий» спавн в стиле Hypixel: интервалы случайны, иногда сразу
	// несколько монет, иногда пауза. Эти границы рулят темпом.
	[Property, Group("Coins")] public float CoinSpawnIntervalMin { get; set; } = 2f;
	[Property, Group("Coins")] public float CoinSpawnIntervalMax { get; set; } = 6f;
	// Шанс «бурста» — за один тик появляется 2-3 монеты сразу.
	[Property, Group("Coins"), Range(0f, 1f)] public float CoinBurstChance { get; set; } = 0.25f;
	[Property, Group("Coins")] public int MaxCoinsOnMap { get; set; } = 15;
	// Минимальная дистанция от уже стоящей монеты, чтобы не дублировать слот.
	[Property, Group("Coins")] public float CoinMinSeparation { get; set; } = 60f;

	// Спавн точки игроков (используются в ResetRoom-рандом позициях).
	[Property] public List<GameObject> SpawnPoints { get; set; } = new();

	[Property] public GameObject LobbyPoint { get; set; }

	// Звуки — назначаешь в инспекторе (имя SoundEvent из проекта).
	// ── Радар мардера ──────────────────────────────────────────────────────
	// Catch-up механика: на поздних этапах матча мардер видит сквозь стены
	// маркеры всех живых мирных. Активируется при одном из двух условий:
	//   1) живых мирных ≤ RadarMaxInnocents И прошло ≥ RadarMinElapsedTime
	//   2) прошло ≥ RadarFallbackElapsedTime (страховка от ничьи по таймеру)
	// Время — секунды, отсчитываются от старта Playing-фазы.
	[Property, Group("Murderer Radar")] public int RadarMaxInnocents { get; set; } = 3;
	[Property, Group("Murderer Radar")] public float RadarMinElapsedTime { get; set; } = 150f;     // 2:30
	[Property, Group("Murderer Radar")] public float RadarFallbackElapsedTime { get; set; } = 240f; // 4:00

	[Sync] public bool MurdererRadarActive { get; set; } = false;

	[Property, Group("Crystal Rewards")] public int CrystalsWin { get; set; } = 100;
	[Property, Group("Crystal Rewards")] public int CrystalsLossSurvived { get; set; } = 50;
	[Property, Group("Crystal Rewards")] public int CrystalsLossDead { get; set; } = 25;
	[Property, Group("Crystal Rewards")] public int CrystalsCaughtMurderer { get; set; } = 75;
	[Property, Group("Crystal Rewards")] public int CrystalsMurdererWon { get; set; } = 50;

	// Антиабуз кристаллов (#11): раунд должен длиться минимум MinRoundDurationForReward секунд
	// И в нём должно было участвовать не менее MinPlayersForReward живых игроков,
	// иначе GrantCrystals не вызывается. Оба порога настраиваются в инспекторе.
	[Property, Group("Crystal Rewards")] public float MinRoundDurationForReward { get; set; } = 60f;
	[Property, Group("Crystal Rewards")] public int   MinPlayersForReward       { get; set; } = 3;

	[Property, Group("Sounds")] public string RoundStartSound { get; set; } = "";
	[Property, Group("Sounds")] public string InnocentsWinSound { get; set; } = "";
	[Property, Group("Sounds")] public string MurdererWinsSound { get; set; } = "";
	[Property, Group("Sounds")] public string CountdownTickSound { get; set; } = "";
	[Property, Group("Sounds")] public string RoundEndSound { get; set; } = "";

	[Sync] public int StateInt { get; set; } = 0;
	[Sync] public float StateTimer { get; set; } = 0f;
	[Sync] public string Winner { get; set; } = "";
	[Sync] public int PlayerCountSynced { get; set; } = 0;
	// Сколько игроков сейчас стоит в очереди матчмейкинга именно ЭТОЙ комнаты.
	// Хост обновляет каждый кадр в MatchmakingQueue. Клиенты используют для
	// баннера поиска: «N/MaxPlayers · M до старта».
	[Sync] public int MatchmakingQueueCount { get; set; } = 0;

	public GameState State => (GameState)StateInt;

	public IEnumerable<PlayerStats> Players =>
		Scene.GetAllComponents<PlayerStats>()
			.Where( p => p.CurrentRoomId == GameObject.Id );

	public int ActivePlayerCount =>
		Players.Count( p => !p.IsSpectator );

	public static IEnumerable<GameRoom> All =>
		Game.ActiveScene.GetAllComponents<GameRoom>();

	private float _coinSpawnTimer;
	private float _stateBroadcastTimer = 0f;
	private int   _lastBroadcastQueueCount = -1; // для немедленного пуша при изменении
	// Монеты, заспавненные именно этой комнатой. Нужен для ResetRoom, чтобы
	// не задеть монеты других GameRoom в той же сцене.
	private readonly List<GameObject> _spawnedCoins = new();

	protected override void OnUpdate()
	{
		// Раньше PlayerCountSynced считался ВСЕМИ клиентами каждый кадр, и
		// каждый GameRoom делал GetAllComponents<PlayerStats>() — при 4 румах
		// это десятки лукапов в кадр. Теперь только хост считает, [Sync]
		// пробрасывает значение клиентам.
		if ( !Networking.IsHost ) return;

		PlayerCountSynced = Players.Count();
		RunHostStateMachine();

		// Раз в секунду пушим текущий timer всем клиентам — иначе на 2-м инстансе
		// HUD не покажет правильный countdown (StateTimer не [Sync]'ается надёжно
		// на scene-static GameObject).
		// Дополнительно: если очередь матчмейкинга изменилась — пушим немедленно,
		// чтобы баннер на non-host обновился сразу (а не висел "0/N" до следующей
		// секунды). Без этого был баг: non-host видел пустую очередь.
		_stateBroadcastTimer += Time.Delta;
		bool queueChanged = MatchmakingQueueCount != _lastBroadcastQueueCount;
		if ( _stateBroadcastTimer >= 1.0f || queueChanged )
		{
			_stateBroadcastTimer = 0f;
			_lastBroadcastQueueCount = MatchmakingQueueCount;
			BroadcastStateChange( StateInt, StateTimer, Winner ?? "", MurdererRadarActive, PlayerCountSynced, MatchmakingQueueCount );
		}
	}

	private void RunHostStateMachine()
	{
		switch ( State )
		{
			case GameState.WaitingForPlayers:
				if ( ActivePlayerCount >= MinPlayers )
				{
					if ( StateTimer <= 0f ) StateTimer = StartCountdown;
					int prevBeep = (int)StateTimer;
					StateTimer -= Time.Delta;
					// Тик каждые 10 секунд во время обратного отсчёта.
					if ( !string.IsNullOrEmpty( CountdownTickSound ) )
					{
						int cur = (int)StateTimer;
						if ( cur != prevBeep && ( cur == 30 || cur == 20 || cur == 10 || cur <= 5 && cur > 0 ) )
							BroadcastSound( CountdownTickSound );
					}
					if ( StateTimer <= 0f )
						ChangeState( GameState.StartingRound );
				}
				else
				{
					StateTimer = 0f;
				}
				break;

			case GameState.StartingRound:
				StateTimer -= Time.Delta;
				if ( StateTimer <= 0f )
				{
					DistributeRoles();
					SpawnInitialCoins();
					ChangeState( GameState.Playing );
				}
				break;

			case GameState.Playing:
				StateTimer -= Time.Delta;
				// Grace-period: первые 2 секунды раунда не проверяем условия победы.
				// Это даёт [Sync] полю IsDead время добежать от owner-клиентов до хоста
				// (иначе при перезапуске раунда хост видит «все мёртвы» и сразу триггерит win).
				if ( PlayingPhaseTime - StateTimer > 2f )
					CheckWinConditions();
				UpdateMurdererRadarFlag( PlayingPhaseTime - StateTimer );
				TickCoinSpawner();
				if ( StateTimer <= 0f )
				{
					// Время вышло, мардер не успел перебить мирных → побеждают мирные.
					Winner = "win.innocents";
					AwardStats( murdererWins: false, Players.Where( p => !p.IsSpectator ).ToList() );
					if ( !string.IsNullOrEmpty( InnocentsWinSound ) )
						BroadcastSound( InnocentsWinSound );
					ChangeState( GameState.RoundEnd );
				}
				break;

			case GameState.RoundEnd:
				StateTimer -= Time.Delta;
				if ( StateTimer <= 0f )
					ChangeState( GameState.WaitingForPlayers );
				break;
		}
	}

	public void ChangeState( GameState newState )
	{
		if ( !Networking.IsHost ) return;

		StateInt = (int)newState;
		switch ( newState )
		{
			case GameState.WaitingForPlayers:
				StateTimer = 0f;
				Winner = "";
				MurdererRadarActive = false;
				ResetRoom();
				RespawnPlayersToLobby();
				break;
			case GameState.StartingRound:
				StateTimer = StartingPhaseTime;
				RespawnPlayersToLobby();
				break;
			case GameState.Playing:
				StateTimer = PlayingPhaseTime;
				// Первый «настоящий» тик спавна монет почти сразу — детали в SpawnInitialCoins.
				// Здесь просто запасной timer на случай если SpawnInitialCoins не вызовется.
				_coinSpawnTimer = Game.Random.Float( 1f, 2f );
				if ( !string.IsNullOrEmpty( RoundStartSound ) )
					BroadcastSound( RoundStartSound );
				break;
			case GameState.RoundEnd:
				StateTimer = RoundEndPhaseTime;
				if ( !string.IsNullOrEmpty( RoundEndSound ) )
					BroadcastSound( RoundEndSound );
				break;
		}

		// Принудительно проталкиваем state ВСЕМ клиентам через RPC — на случай
		// если [Sync] не работает для scene-static GameObject (известная нюанс
		// в s&box: [Sync] стабилен только для network-spawned объектов).
		BroadcastStateChange( StateInt, StateTimer, Winner ?? "", MurdererRadarActive, PlayerCountSynced, MatchmakingQueueCount );
	}

	/// <summary>
	/// Хост → все клиенты: принудительная синхронизация состояния комнаты.
	/// Дублирует [Sync]-поля, потому что для scene-static объектов Sync может
	/// не пробрасываться.
	/// </summary>
	[Rpc.Broadcast]
	private void BroadcastStateChange( int state, float timer, string winner, bool radarActive, int playerCount, int queueCount )
	{
		var caller = Rpc.Caller;
		if ( caller != null && !caller.IsHost ) return;

		StateInt = state;
		StateTimer = timer;
		Winner = winner;
		MurdererRadarActive = radarActive;
		PlayerCountSynced = playerCount;
		MatchmakingQueueCount = queueCount;
	}

	// ── Монеты ──────────────────────────────────────────────────────────────

	private void SpawnInitialCoins()
	{
		SpawnCoinsAtPoints( InitialCoins );
		// Первый «настоящий» тик — почти сразу после старта (как у Hypixel).
		_coinSpawnTimer = Game.Random.Float( 1f, 2f );
	}

	private void TickCoinSpawner()
	{
		_coinSpawnTimer -= Time.Delta;
		if ( _coinSpawnTimer > 0f ) return;

		// «Пульс»: чаще одна монета, иногда бурст 2-3.
		int count = 1;
		if ( Game.Random.Float() < CoinBurstChance )
			count = Game.Random.Int( 2, 3 );

		SpawnCoinsAtPoints( count );

		// Следующий интервал — случайный в заданных границах.
		// Если карта забита под лимит — увеличиваем паузу (нет смысла часто пытаться).
		int onMap = CountCoinsInRoom();
		float min = CoinSpawnIntervalMin;
		float max = CoinSpawnIntervalMax;
		if ( onMap >= MaxCoinsOnMap )
		{
			min = System.Math.Max( min, 4f );
			max = System.Math.Max( max, 8f );
		}
		_coinSpawnTimer = Game.Random.Float( min, max );
	}

	private void SpawnCoinsAtPoints( int count )
	{
		if ( CoinPrefab == null || CoinSpawnPoints.Count == 0 ) return;

		int current = CountCoinsInRoom();
		int toSpawn = System.Math.Min( count, MaxCoinsOnMap - current );
		if ( toSpawn <= 0 ) return;

		PruneSpawnedCoins();

		// Список текущих координат монет в этой комнате — чтобы не спавнить поверх.
		var existing = _spawnedCoins
			.Select( g => g.WorldPosition )
			.ToList();

		// Точки, на которых сейчас НЕТ монеты, перетасованные.
		var available = CoinSpawnPoints
			.Where( p => p != null )
			.Where( p => !existing.Any( ep => ep.Distance( p.WorldPosition ) < CoinMinSeparation ) )
			.OrderBy( _ => Game.Random.Int( 0, 9999 ) )
			.ToList();

		for ( int i = 0; i < toSpawn && i < available.Count; i++ )
		{
			var coin = CoinPrefab.Clone( available[i].WorldPosition );

			// Помечаем монету «принадлежит этой руме» ДО NetworkSpawn —
			// клиенты получат RoomId в начальном sync и сразу решат, рендерить
			// её / анимировать или нет (см. Coin.OnUpdate).
			var coinComp = coin.GetComponent<Coin>();
			if ( coinComp != null ) coinComp.RoomId = GameObject.Id;

			coin.NetworkSpawn();
			_spawnedCoins.Add( coin );
		}
	}

	private int CountCoinsInRoom()
	{
		PruneSpawnedCoins();
		return _spawnedCoins.Count;
	}

	// Убирает уничтоженные/собранные монеты из списка _spawnedCoins.
	private void PruneSpawnedCoins()
	{
		_spawnedCoins.RemoveAll( g => g == null || !g.IsValid );
	}

	// ── Роли / победа ───────────────────────────────────────────────────────

	private void DistributeRoles()
	{
		var players = Players
			.Where( p => !p.IsSpectator )
			.OrderBy( _ => Game.Random.Int( 0, 9999 ) )
			.ToList();

		if ( players.Count == 0 ) return;

		foreach ( var p in players )
			p.AssignRole( PlayerStats.PlayerRole.Innocent );

		players[0].AssignRole( PlayerStats.PlayerRole.Murderer );

		if ( players.Count > 2 )
		{
			players[1].AssignRole( PlayerStats.PlayerRole.Detective );
			players[1].GrantWeapon();
		}

	}

	/// <summary>
	/// Catch-up радар для мардера. На хосте: считаем живых мирных и сколько
	/// времени прошло с начала Playing-фазы; включаем флаг при достижении
	/// одного из триггеров. Клиенты получат значение через [Sync].
	/// </summary>
	private void UpdateMurdererRadarFlag( float elapsed )
	{
		var roundPlayers = Players.Where( p => !p.IsSpectator ).ToList();
		int innocentsAlive = roundPlayers.Count( p =>
			!p.IsDead && p.Role != PlayerStats.PlayerRole.Murderer );

		bool byCount = innocentsAlive <= RadarMaxInnocents
		            && elapsed >= RadarMinElapsedTime;
		bool byTime  = elapsed >= RadarFallbackElapsedTime;

		bool shouldBeOn = byCount || byTime;
		if ( MurdererRadarActive != shouldBeOn )
			MurdererRadarActive = shouldBeOn;
	}

	private void CheckWinConditions()
	{
		if ( State != GameState.Playing ) return;

		var roundPlayers = Players.Where( p => !p.IsSpectator ).ToList();
		var alive = roundPlayers.Where( p => !p.IsDead ).ToList();

		// Все ушли (например, последний игрок дисконнектнулся) — сбрасываем раунд.
		if ( roundPlayers.Count == 0 )
		{
			ChangeState( GameState.WaitingForPlayers );
			return;
		}

		// Раньше тут был early-return "если все живы — ничего не проверяем".
		// Это ломало детект выхода из игры: если убийца дисконнектнулся когда все
		// живы, его не было ни в alive, ни в roundPlayers — значит alive.Count
		// == roundPlayers.Count и проверка пропускалась. Теперь всегда проверяем
		// состав ролей, чтобы выход игрока тоже триггерил победу.
		bool murdererAlive  = alive.Any( p => p.Role == PlayerStats.PlayerRole.Murderer );
		bool innocentsAlive = alive.Any( p => p.Role != PlayerStats.PlayerRole.Murderer );

		if ( !murdererAlive )
		{
			Winner = "win.innocents";
			AwardStats( murdererWins: false, roundPlayers );
			if ( !string.IsNullOrEmpty( InnocentsWinSound ) )
				BroadcastSound( InnocentsWinSound );
			ChangeState( GameState.RoundEnd );
		}
		else if ( !innocentsAlive )
		{
			Winner = "win.murderer";
			AwardStats( murdererWins: true, roundPlayers );
			if ( !string.IsNullOrEmpty( MurdererWinsSound ) )
				BroadcastSound( MurdererWinsSound );
			ChangeState( GameState.RoundEnd );
		}
	}

	/// <summary>
	/// Сбалансированная экономика наград за раунд. Считаем разбивку по категориям
	/// и пушим её каждому игроку через SetRoundRewards — UI потом отрисует список
	/// со всеми пунктами (включая нули) и финальной суммой.
	///
	/// Базовые (всем):
	///   • Завершение матча       — +40
	///   • Время жизни            — +1 за каждые полные 30 сек живой игры
	///
	/// Innocent / Detective:
	///   • Сбор слитка            — +4 за штуку (RoundCoinsPickedUp)
	///   • Убийство мардера       — +80 (Innocent) / +120 (Detective) за каждое
	///   • Победа команды (жив)   — +80
	///   • Победа команды (мёртв) — +25
	///
	/// Murderer:
	///   • Убийство игрока        — +12 за каждое (RoundKills)
	///   • Идеальная победа       — +100 (если мардер выиграл раунд)
	/// </summary>
	private void AwardStats( bool murdererWins, List<PlayerStats> roundPlayers )
	{
		// Антиабуз (#11): кристаллы выдаём только если раунд длился достаточно долго
		// и в нём участвовало достаточно игроков. Протектит от фармилок с сокс-аккаунтами.
		float elapsed         = PlayingPhaseTime - StateTimer;
		int   activePlayers   = roundPlayers.Count( p => !p.IsSpectator );
		bool  crystalsAllowed = elapsed >= MinRoundDurationForReward
		                     && activePlayers >= MinPlayersForReward;

		foreach ( var p in roundPlayers )
		{
			bool isMurd = p.Role == PlayerStats.PlayerRole.Murderer;
			bool isDet  = p.Role == PlayerStats.PlayerRole.Detective;
			bool won    = murdererWins ? isMurd : !isMurd;

			// ── Базовые (для всех) ─────────────────────────────────────────
			int matchBonus     = 40;
			int aliveTimeBonus = (int)(p.RoundSurvivedSeconds / 30f);  // +1 за каждые полные 30 сек

			// ── Роль-специфичные ───────────────────────────────────────────
			int coinsBonus      = 0;
			int killMurdBonus   = 0;
			int teamWinBonus    = 0;
			int killsBonus      = 0;
			int murdPerfBonus   = 0;

			if ( !isMurd )
			{
				// Innocent или Detective
				coinsBonus = p.RoundCoinsPickedUp * 4;

				int perKill = isDet ? 120 : 80;
				killMurdBonus = p.RoundMurdererKills * perKill;

				if ( won )
					teamWinBonus = p.IsDead ? 25 : 80;
			}
			else
			{
				// Murderer
				killsBonus = p.RoundKills * 12;
				if ( won ) murdPerfBonus = 100;
			}

			// ── Глобальная статистика ──────────────────────────────────────
			if ( won ) p.IncrementStat( "wins", 1 );
			if ( !p.IsDead ) p.IncrementStat( "survived", 1 );
			if ( isDet ) p.IncrementStat( "detective", 1 );

			// ── Ачивки ─────────────────────────────────────────────────────
			try { Sandbox.Services.Achievements.Unlock( "mm_first_match" ); } catch { }
			if ( isMurd && won && !p.IsDead )
				try { Sandbox.Services.Achievements.Unlock( "mm_perfect_murde" ); } catch { }

			// ── Пуш разбивки игроку (для UI) ───────────────────────────────
			// Если матч не засчитан (короткий или мало игроков) — пушим нули,
			// чтобы UI конца раунда не показывал «фиктивные» кристаллы.
			if ( crystalsAllowed )
			{
				p.SetRoundRewards( matchBonus, aliveTimeBonus, coinsBonus,
					killMurdBonus, teamWinBonus, killsBonus, murdPerfBonus );
			}
			else
			{
				p.SetRoundRewards( 0, 0, 0, 0, 0, 0, 0 );
			}

			// ── Отметка «сыграл партию сегодня» (для гейта ежедневной награды) ─
			p.MarkPlayedToday();

			// ── Начисление кристаллов ──────────────────────────────────────
			int total = matchBonus + aliveTimeBonus + coinsBonus
				+ killMurdBonus + teamWinBonus + killsBonus + murdPerfBonus;
			if ( crystalsAllowed && total > 0 ) p.GrantCrystals( total );
		}
	}

	// ── Ресет ───────────────────────────────────────────────────────────────

	private void ResetRoom()
	{
		var lobbyPos = LobbyPoint?.WorldPosition ?? WorldPosition;
		const float roomRadius = 3000f;

		PruneSpawnedCoins();
		foreach ( var coin in _spawnedCoins.ToList() )
		{
			coin.Destroy();
		}
		_spawnedCoins.Clear();
		foreach ( var body in Scene.GetAllComponents<RagdollBody>().ToList() )
		{
			if ( body.GameObject.WorldPosition.Distance( lobbyPos ) < roomRadius )
				body.GameObject.Destroy();
		}

		foreach ( var p in Players )
			p.ResetForNewRound();
	}

	private void RespawnPlayersToLobby()
	{
		var lobbyPos = LobbyPoint?.WorldPosition;
		if ( lobbyPos == null ) return;

		// Равномерно распределяем игроков по кругу — каждый получает свой угол,
		// чтобы никто не попал в одну точку и не отлетал при прыжке.
		var playersList = Players.ToList();
		int count = playersList.Count;
		for ( int i = 0; i < count; i++ )
		{
			float angle = i * (360f / System.Math.Max( count, 1 ));
			var offset = Rotation.FromYaw( angle ).Forward * 150f;
			playersList[i].TeleportTo( lobbyPos.Value + offset );
		}
	}

	// ── Звуки (broadcast → все клиенты) ─────────────────────────────────────

	[Rpc.Broadcast]
	public void BroadcastSound( string soundEvent )
	{
		if ( string.IsNullOrEmpty( soundEvent ) ) return;

		// Принимающий клиент проигрывает звук только если его локальный игрок
		// сейчас находится в этой руме (или присоединён как спектатор именно сюда).
		// Иначе игроки в других румах/хабе слышали бы чужие победные фанфары.
		var local = Scene?.GetAllComponents<PlayerStats>()?.FirstOrDefault( p => !p.IsProxy );
		if ( local == null ) return;
		if ( local.CurrentRoomId != GameObject.Id ) return;

		Sfx.Play( soundEvent );
	}
}
