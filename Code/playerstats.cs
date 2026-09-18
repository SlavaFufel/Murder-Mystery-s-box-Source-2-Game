using Sandbox;
using System.Linq;

public sealed class PlayerStats : Component
{
	public enum PlayerRole { Innocent, Murderer, Detective }

	[Property, Sync] public PlayerRole Role { get; set; } = PlayerRole.Innocent;
	[Property, Sync] public int Coins { get; set; } = 0;
	[Property, Sync] public bool HasWeapon { get; set; } = false;
	[Property, Sync] public bool IsDead { get; set; } = false;

	// Persistent crystal currency (saved to stats.json across sessions).
	// У новых игроков 0 — balance через ежедневки/раунды/промо.
	[Property, Sync] public int Crystals { get; set; } = 0;

	// Synced equipped cosmetics serialized as "slot:id;slot:id" so other clients
	// можно перерисовать чужой визуал. Helpers: GetEquipped / SetEquipped.
	[Sync] public string EquippedCosmeticsSerialized { get; set; } = "";

	// Owned cosmetics — локальный, не синкаем (нужно только владельцу для UI магазина).
	// Ключи слотов: "knife" / "back" / "hat" / "nickname".
	// Значение: itemId → количество (≥1). Дубликаты из кейсов хранятся как count > 1.
	private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, int>> _owned = new();

	// Owned cases — каждый id кейса → количество. Локальный, синк не нужен.
	private readonly System.Collections.Generic.Dictionary<string, int> _ownedCases = new();

	// Использованные промокоды (нормализованные lowercase trim). Локальные,
	// синк не нужен — это инвентарь самого игрока. Персистится в stats.json.
	private readonly System.Collections.Generic.HashSet<string> _redeemedPromos = new();

	// UI читает эти три поля чтобы показать тост о результате активации
	// промокода. Counter инкрементится — реактивно ловим даже повторный
	// фидбек одного и того же текста.
	public string LastPromoFeedbackKey { get; private set; } = "";
	public string LastPromoFeedbackArg { get; private set; } = "";
	public bool LastPromoSuccess { get; private set; } = false;
	public int PromoFeedbackCounter { get; private set; } = 0;

	// ── Ежедневки ───────────────────────────────────────────────────────────
	// UTC-дата последнего получения ("yyyy-MM-dd"). Локальное per-player поле.
	public string LastDailyClaimDate { get; private set; } = "";
	// Какой день стрика был забран последним (1..7). 0 = ещё не забирал.
	public int DailyClaimStreak { get; private set; } = 0;
	// UTC-дата последней сыгранной партии ("yyyy-MM-dd"). Награду за день
	// можно забрать только если эта дата == сегодня.
	public string LastGamePlayedDate { get; private set; } = "";
	// Фидбек для DailyRewardsUI (по аналогии с промокодами).
	public string LastDailyFeedbackKey { get; private set; } = "";
	public string LastDailyFeedbackArg { get; private set; } = "";
	public bool LastDailySuccess { get; private set; } = false;
	public int DailyFeedbackCounter { get; private set; } = 0;

	// Результат последнего открытого кейса. UI (CaseOpeningUI) читает их и проигрывает анимацию.
	// LastRolledItemId — id косметики из CosmeticCatalog. LastRolledCaseId — какого кейса открывали.
	// LastRollCounter инкрементится при каждом ролле — UI смотрит на счётчик чтобы не пропустить
	// повторный ролл того же предмета подряд.
	[Sync] public string LastRolledItemId { get; set; } = "";
	[Sync] public string LastRolledCaseId { get; set; } = "";
	[Sync] public int LastRollCounter { get; set; } = 0;

	[Sync] public bool IsGameHost { get; set; } = false;
	[Sync] public int HostGameState { get; set; } = 0;
	[Sync] public float HostGameTimer { get; set; } = 0f;
	[Sync] public string HostGameWinner { get; set; } = "";

	// Persistent per-session scoreboard stats.
	[Sync] public int StatWins { get; set; } = 0;
	[Sync] public int StatKills { get; set; } = 0;
	[Sync] public int StatSurvived { get; set; } = 0;      // rounds survived
	[Sync] public int StatRoundsAsDetective { get; set; } = 0;
	[Sync] public int StatMurdererCatches { get; set; } = 0; // times this player killed the Murderer

	// Round-scoped stats — обнуляются в ResetForNewRound. Используются в RoundEndUI.
	[Sync] public int RoundKills { get; set; } = 0;
	[Sync] public float RoundSurvivedSeconds { get; set; } = 0f;
	[Sync] public int RoundCoinsPickedUp { get; set; } = 0;     // сколько монет собрал именно за этот раунд
	[Sync] public int RoundMurdererKills { get; set; } = 0;     // сколько раз ЭТОТ игрок завалил мардера в раунде

	// Награды раунда — выставляются хостом через SetRoundRewards RPC,
	// читаются локальным RoundEndUI для отображения breakdown'а.
	// Не [Sync] — это per-player значения, прокатываются точечно RPC'шкой.
	public int RewardMatchComplete   { get; set; } = 0;
	public int RewardAliveTime       { get; set; } = 0;
	public int RewardCoins           { get; set; } = 0;
	public int RewardMurdererKilled  { get; set; } = 0;
	public int RewardTeamWin         { get; set; } = 0;
	public int RewardKills           { get; set; } = 0;
	public int RewardMurdererPerfect { get; set; } = 0;
	public int RewardTotal => RewardMatchComplete + RewardAliveTime + RewardCoins
		+ RewardMurdererKilled + RewardTeamWin + RewardKills + RewardMurdererPerfect;

	[Property] public float MurdererAttackCooldown { get; set; } = 5f;
	private float _murdererCooldownLeft = 0f;
	public float MurdererCooldownLeft => _murdererCooldownLeft;

	// Запускает кд ножа (вызывается из KnifeWeapon после удара).
	public void TriggerMurdererCooldown()
	{
		_murdererCooldownLeft = MurdererAttackCooldown;
	}

	[Property] public float DetectiveShotCooldown { get; set; } = 5f;
	private float _detectiveCooldownLeft = 0f;
	public float DetectiveCooldownLeft => _detectiveCooldownLeft;

	// Кулдаун выстрела в тире (зона ожидания). Не влияет на роль/раунд —
	// просто тренировочный темп стрельбы. Не больно «строгий» — 1 секунда.
	[Property] public float TirShotCooldown { get; set; } = 1f;
	private float _tirCooldownLeft = 0f;
	public float TirCooldownLeft => _tirCooldownLeft;

	// Счётчик попаданий в тире — сетится при удачном hit на TirTarget.
	// Накапливается за сессию, отображается на тир-лидерборде. НЕ глобальный
	// stat (в облако не идёт), просто синкаемый интовый счётчик.
	[Sync] public int TirHits { get; set; } = 0;

	// Текущий стрик в тире — растёт при попаданиях подряд в окне TirStreakTimeout.
	// Сбрасывается при промахе или таймауте. Не глобальный (sync только для UI).
	[Sync] public int TirCurrentStreak { get; set; } = 0;

	// Лучший стрик за всё время. ГЛОБАЛЬНЫЙ — пушится в Sandbox.Services и
	// сохраняется в StatsPersistence. Загружается при логине.
	[Sync] public int StatTirBestStreak { get; set; } = 0;

	// Окно времени между хитами чтобы стрик не оборвался. Если пройдёт больше
	// этого времени без попадания — стрик сбрасывается.
	[Property] public float TirStreakTimeout { get; set; } = 1.5f;

	// Время последнего попадания (Time.Now). Локально на стрелке, не синкается.
	private float _lastTirHitTime = 0f;

	// Стамина (#7): роль-зависимые параметры.
	[Property, Group("Stamina")] public float MurdererStaminaDuration  { get; set; } = 13f;
	[Property, Group("Stamina")] public float DetectiveStaminaDuration { get; set; } = 10f;
	[Property, Group("Stamina")] public float InnocentStaminaDuration  { get; set; } = 7f;
	[Property, Group("Stamina")] public float MurdererSprintMult       { get; set; } = 1.4f;
	[Property, Group("Stamina")] public float DetectiveSprintMult      { get; set; } = 1.3f;
	[Property, Group("Stamina")] public float InnocentSprintMult       { get; set; } = 1.25f;
	// Стоимость прыжка: доля от максимальной стамины (0.25 = 25%).
	[Property, Group("Stamina")] public float JumpStaminaCost          { get; set; } = 0.1f;
	// Блокировка атаки Убийцы сразу после истощения (секунды).
	[Property, Group("Stamina")] public float ExhaustedAttackDelay     { get; set; } = 0.75f;
	// Скорость ходьбы при истощении (u/s). Настрой под WalkSpeed PlayerController в префабе.
	[Property, Group("Stamina")] public float StaminaWalkSpeed         { get; set; } = 150f;
	[Property, Group("Stamina")] public float StaminaRegenRate         { get; set; } = 1f;
	[Property, Group("Stamina")] public float StaminaRegenThreshold    { get; set; } = 1f;
	// Задержка перед началом регенерации после того как игрок перестал бежать.
	// Регенерируется и при ходьбе, и стоя — лишь бы не нажат shift.
	[Property, Group("Stamina")] public float StaminaRegenDelay        { get; set; } = 1f;

	private float _staminaCurrent = -1f; // -1 = needs init
	private bool  _staminaExhausted = false;
	// Сколько секунд игрок уже НЕ спринтит (для StaminaRegenDelay). Обнуляется
	// при каждом кадре спринта.
	private float _notSprintingFor = 0f;
	private Vector3 _lastWishDir = Vector3.Zero;
	private float _exhaustedAttackBlock = 0f;
	private float _originalRunSpeed = -1f;

	float CurrentMaxStamina => Role switch
	{
		PlayerRole.Murderer  => MurdererStaminaDuration,
		PlayerRole.Detective => DetectiveStaminaDuration,
		_                    => InnocentStaminaDuration,
	};
	float CurrentSprintMult => Role switch
	{
		PlayerRole.Murderer  => MurdererSprintMult,
		PlayerRole.Detective => DetectiveSprintMult,
		_                    => InnocentSprintMult,
	};

	public float StaminaNormalized => System.Math.Clamp(
		_staminaCurrent / System.Math.Max( CurrentMaxStamina, 0.001f ), 0f, 1f );
	public bool StaminaExhausted => _staminaExhausted;
	// True пока Убийца не может атаковать из-за только что истощившейся стамины.
	public bool StaminaAttackBlocked => _exhaustedAttackBlock > 0f;

	// Туториал (#24): показывается новым игрокам при первом входе.
	public bool TutorialVisible { get; set; } = false;

	// ADS: пистолет на ПКМ. Локально — IsAds выставляется в OnUpdate, читается
	// FirstPersonHeldItemOffset (поза пистолета) и HUD (показ прицела).
	// Не [Sync]: ADS-зум и прицел нужны только локально, передавать другим
	// игрокам не имеет смысла (они зум не видят, поза пистолета у них одна).
	public bool IsAds { get; set; } = false;
	[Property, Group( "ADS" )] public float AdsFovZoom { get; set; } = 25f; // на сколько уменьшить FOV
	[Property, Group( "ADS" )] public float AdsFovLerpSpeed { get; set; } = 14f;
	private float _baseFov = -1f;

	// Локальный гард от двойного срабатывания Kill() — перекрывает [Sync]-задержку IsDead у прокси.
	private bool _killGuard = false;

	// Legacy visuals — retained for backwards compatibility.
	// New inventory system uses ItemVisibilityController.
	[Property] public GameObject MurdererWeaponVisual { get; set; }
	[Property] public GameObject InnocentWeaponVisual { get; set; }
	[Property] public GameObject BulletTracerPrefab { get; set; }

	// Звуки — назначай в инспекторе Player prefab.
	[Property, Group("Sounds")] public string GunShotSound { get; set; } = "";
	[Property, Group("Sounds")] public string DeathSound { get; set; } = "";

	// Prefab for the gun the Detective drops on death (has DetectiveGunPickup).
	[Property] public GameObject DetectiveGunPickupPrefab { get; set; }

	// Ragdoll prefab — spawned at death position. See RagdollBody.
	[Property] public GameObject RagdollPrefab { get; set; }

	// Joined mid-round / server full → permanent spectator for current round (or forever if capped).
	[Sync] public bool IsSpectator { get; set; } = false;

	// True когда игрок находится в активной торговой сессии — синкается чтобы
	// другие игроки видели статус и не отправляли приглашение занятому.
	[Sync] public bool IsTrading { get; set; } = false;

	// К какой GameRoom принадлежит игрок (Guid GameObject'а комнаты).
	// Default.Empty = ещё не выбрал комнату, висит в хабе.
	[Sync] public System.Guid CurrentRoomId { get; set; } = System.Guid.Empty;

	// Отображаемое имя (steam-ник) — для нейм-плейтов над головой и т.п.
	// Заполняется владельцем в OnStart() и синкается остальным.
	[Sync] public string DisplayName { get; set; } = "";

	// Активная эмоция над головой. Владелец пишет через RPC PlayEmote, Sync
	// разносит на всех. EmoteOverheadUI смотрит на Time.Now < ActiveEmoteEndAt.
	[Sync] public string ActiveEmoteId  { get; set; } = "";
	[Sync] public float  ActiveEmoteEndAt { get; set; } = 0f;

	// 6 эмоций колеса игрока, через ';' (см. GetEmoteSlot/SetEmoteSlotLocal).
	// Пустая строка / меньше 6 элементов → используем CosmeticCatalog.DefaultEmoteWheel.
	[Sync] public string EmoteWheelSerialized { get; set; } = "";

	protected override void OnStart()
	{
		if ( !IsProxy && Networking.IsHost )
			IsGameHost = true;

		// Чистим внутриигровую консоль локальному игроку, чтобы красные ошибки
		// движка/прошлой сессии не маячили перед глазами.
		if ( !IsProxy )
		{
			try { Sandbox.ConsoleSystem.Run( "clear" ); } catch { }
		}

		// Владелец сразу выставляет своё отображаемое имя из Connection-а,
		// чтобы остальные клиенты увидели его в нейм-плейтах.
		if ( !IsProxy )
		{
			try
			{
				var n = Network.Owner?.DisplayName;
				if ( !string.IsNullOrWhiteSpace( n ) )
					DisplayName = n;
			}
			catch { }
		}

		// Variant 3 (гибрид): каждый owner САМ грузит свой stats.json локально через
		// FileSystem.Data — это per-player хранилище s&box, у каждого игрока на ПК.
		// Хост больше не центральный авторитет по статам — это нужно потому, что
		// клиенты теперь хотят переносить инвентарь между разными серверами.
		// Лидерборд (wins/kills) дополнительно пушится в Sandbox.Services.Stats —
		// это даёт глобальный топ, видный даже когда игрок офлайн.
		// ВАЖНО: ApplyTo должен идти ДО ApplyClothing — иначе экипированная
		// косметика (hat/back) не применится при первом спавне.
		if ( !IsProxy )
		{
			StatsPersistence.ApplyTo( this, GetSteamIdString() );
			GrantStarterCaseIfFirstJoin();
			// Синхронизируем локальные статы с глобальным лидербордом (#19/#26).
			// Если предыдущие пуши прерывались (потеря сети, краш), это выравнивает расхождение.
			SyncLocalStatsToLeaderboard();
			// Туториал (#24): показать новым игрокам.
			InitTutorial( GetSteamIdString() );
			// Облачная синхронизация (#DB): загружаем инвентарь из Kv.
			// Запускается после локального ApplyTo — локальные данные уже применены
			// и игрок может сразу взаимодействовать. Через ~1-2 сек придут облачные
			// данные и перезапишут локальные (облако авторитетнее).
			_ = SyncFromCloudAsync();
		}

		// Применяем кастомизированный citizen-аватар игрока (одежда, цвет кожи и т.д.)
		// из его профиля s&box. Без этого все ходят дефолтным citizen.
		ApplyClothing();
		// Синхронизируем watch-кэш чтобы первый OnUpdate не дёрнул ApplyClothing повторно.
		_lastAppliedEquippedClothing = EquippedCosmeticsSerialized ?? "";
	}

	/// <summary>
	/// Выдаёт стартовый кейс при первом заходе (один раз навсегда). Флаг
	/// HasReceivedStarterCase сохраняется в StatEntry — повторно не сработает.
	/// </summary>
	private void GrantStarterCaseIfFirstJoin()
	{
		var sid = GetSteamIdString();
		if ( string.IsNullOrEmpty( sid ) ) return;
		var entry = StatsPersistence.GetFor( sid );
		if ( entry.HasReceivedStarterCase ) return;

		AddCaseLocal( StarterCase.Id, 1 );
		entry.HasReceivedStarterCase = true;
		StatsPersistence.StoreFrom( this, sid );
		Log.Info( $"[StarterCase] '{GameObject.Name}' got their starter case." );
	}

	private string GetSteamIdString()
	{
		try { return Network.Owner?.SteamId.ToString(); } catch { return null; }
	}

	private void ApplyClothingItemFor( ClothingContainer clothing, string slot )
	{
		var id = GetEquipped( slot );
		if ( string.IsNullOrEmpty( id ) ) return;
		var item = CosmeticCatalog.Get( id );
		if ( item == null ) return;
		if ( item.Method != CosmeticCatalog.ApplyMethod.Clothing ) return;
		if ( string.IsNullOrEmpty( item.ResourceRef ) ) return;
		try
		{
			var clothingAsset = ResourceLibrary.Get<Clothing>( item.ResourceRef );
			if ( clothingAsset != null )
				clothing.Toggle( clothingAsset );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Clothing] {slot}={id} не применилась: {ex.Message}" );
		}
	}

	private void ApplyClothing()
	{
		var bodyRenderer = Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		if ( bodyRenderer == null )
		{
			Log.Warning( $"[CLOTHING] ApplyClothing: no body renderer on player={DisplayName}" );
			return;
		}

		var effective = GetEffectiveEquippedSerialized() ?? "";
		var hatId = GetEquipped( "hat" );
		var backId = GetEquipped( "back" );
		Log.Info( $"[COSM] ApplyClothing player={DisplayName} IsProxy={IsProxy} hat='{hatId}' back='{backId}' eff='{effective}' sync='{EquippedCosmeticsSerialized}'" );

		try
		{
			// На локальной стороне берём аватар из своего профиля; на сетевых
			// клиентах — из Connection владельца этого GameObject.
			var owner = Network.Owner;
			var clothing = owner != null
				? ClothingContainer.CreateFromJson( owner.GetUserData( "avatar" ) )
				: ClothingContainer.CreateFromLocalUser();

			// Накладываем выбранную косметику (шляпа / спина / др.) поверх дефолтного аватара,
			// если у предмета Method == Clothing.
			if ( clothing != null )
			{
				ApplyClothingItemFor( clothing, "hat" );
				ApplyClothingItemFor( clothing, "back" );
			}

			clothing?.Apply( bodyRenderer );

			// Аватар мог добавить новые рендереры (шляпы, и т.п.) — сбрасываем кэш.
			InvalidateRendererCache();

			// Bone-attached аксессуары (крылья, плащи, рюкзаки) живут вне
			// ClothingContainer. Триггерим их пересборку после Apply, иначе
			// они пропадают когда clothing.Reset уничтожает детей body.
			var accessoryController = Components.Get<AccessoryController>();
			accessoryController?.Rebuild();
			Log.Info( $"[COSM] ApplyClothing DONE player={DisplayName} IsProxy={IsProxy}" );
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"[Clothing] Не удалось применить аватар: {e.Message}" );
		}
	}

	// Кэшированные списки рендереров — иначе UpdateRendererVisibility делал
	// Components.GetAll<…>() каждый кадр для каждого игрока, что аллоцировало
	// листы и было дорого при N игроках на N клиентов.
	private System.Collections.Generic.List<ModelRenderer> _cachedRenderers;
	private System.Collections.Generic.List<SkinnedModelRenderer> _cachedSkinned;
	private System.Collections.Generic.List<Collider> _cachedColliders;
	private Sandbox.PlayerController _cachedController;
	private bool? _lastVisibleState;
	private bool? _lastCollidersEnabled;
	private bool? _lastControllerEnabled;

	private void EnsureRendererCache()
	{
		if ( _cachedRenderers == null )
			_cachedRenderers = Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ).ToList();
		if ( _cachedSkinned == null )
			_cachedSkinned = Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ).ToList();
	}

	private void EnsureColliderCache()
	{
		if ( _cachedColliders == null )
			_cachedColliders = Components.GetAll<Collider>( FindMode.EverythingInSelfAndDescendants ).ToList();
	}

	private Sandbox.PlayerController GetCachedController()
	{
		if ( _cachedController == null || !_cachedController.IsValid )
			_cachedController = Components.Get<Sandbox.PlayerController>();
		return _cachedController;
	}

	/// <summary>
	/// Отключает/включает Sandbox.PlayerController у прокси, который висит вне
	/// локальной румы. Это самый дорогой компонент: он каждый кадр делает sweep
	/// капсулы, и при 32 игроках в 4 румах все клиенты симулируют 32 капсулы
	/// вместо 8 «своих». Не трогаем мёртвых — у них pc уже выключен через
	/// FreezeDeadCharacter и должен оставаться выключенным.
	/// </summary>
	private void SetControllerEnabledForScope( bool enabled )
	{
		if ( IsDead && enabled ) return;
		if ( _lastControllerEnabled == enabled ) return;
		var pc = GetCachedController();
		if ( pc == null || !pc.IsValid ) return;
		_lastControllerEnabled = enabled;
		try { pc.Enabled = enabled; } catch { }
	}

	private void InvalidateRendererCache()
	{
		_cachedRenderers = null;
		_cachedSkinned = null;
		_lastVisibleState = null;
	}

	/// <summary>
	/// Отключает/включает все коллайдеры тела — чтобы хитбокс мёртвого игрока
	/// не ловил пули/нож, и в труп нельзя было упереться. Хитбоксы для трейса
	/// (UseHitboxes) тоже отрубаются вместе с физическими шейпами модели.
	/// </summary>
	private void SetCollidersEnabled( bool enabled )
	{
		if ( _lastCollidersEnabled == enabled ) return;
		_lastCollidersEnabled = enabled;
		EnsureColliderCache();
		foreach ( var c in _cachedColliders )
			if ( c != null && c.IsValid && c.Enabled != enabled ) c.Enabled = enabled;
	}

	private void SetRenderersEnabled( bool visible )
	{
		if ( _lastVisibleState == visible ) return;
		_lastVisibleState = visible;
		EnsureRendererCache();
		foreach ( var r in _cachedRenderers )
			if ( r != null && r.IsValid && r.Enabled != visible ) r.Enabled = visible;
		foreach ( var r in _cachedSkinned )
			if ( r != null && r.IsValid && r.Enabled != visible ) r.Enabled = visible;
	}

	protected override void OnUpdate()
	{
		// Room scope: если этот игрок (прокси) висит в другой руме, чем
		// локальный — отключаем у него рендереры (не анимируется citizen-ригом)
		// и выходим. Это главный источник просадок ФПС когда играют 4 румы:
		// раньше у каждого клиента в каждом кадре оживали все 32 citizen
		// модели, а нужны только 8 «своих».
		bool inLocalScope = !IsProxy || GameManager.IsInLocalScope( CurrentRoomId );
		if ( !inLocalScope )
		{
			SetRenderersEnabled( false );
			// Глушим физику чужого прокси: иначе Sandbox.PlayerController каждый кадр
			// свипует капсулу, и FPS у всех клиентов падает квадратично от числа рум.
			SetCollidersEnabled( false );
			SetControllerEnabledForScope( false );
			if ( MurdererWeaponVisual != null && MurdererWeaponVisual.Enabled )
				MurdererWeaponVisual.Enabled = false;
			if ( InnocentWeaponVisual != null && InnocentWeaponVisual.Enabled )
				InnocentWeaponVisual.Enabled = false;
			return;
		}

		// Прокси вернулся в нашу руму — оживляем коллайдеры/контроллер
		// (для не-мёртвых; FreezeDeadCharacter держит pc выключенным сам).
		if ( IsProxy && !IsDead )
		{
			SetCollidersEnabled( true );
			SetControllerEnabledForScope( true );
		}

		// Если экипировка изменилась (sync пришёл) — перенакатим аватар.
		// Нужно как backup-механизм если RPC не дошёл / не сработал.
		// Без этого снятие косметики не видно у других игроков.
		var curEquipped = EquippedCosmeticsSerialized ?? "";
		if ( curEquipped != _lastAppliedEquippedClothing )
		{
			Log.Info( $"[COSM] watch trigger player={DisplayName} IsProxy={IsProxy} old='{_lastAppliedEquippedClothing}' new='{curEquipped}'" );
			_lastAppliedEquippedClothing = curEquipped;
			ApplyClothing();
		}

		if ( !IsProxy )
		{
			if ( _murdererCooldownLeft > 0f )
				_murdererCooldownLeft -= Time.Delta;
			if ( _detectiveCooldownLeft > 0f )
				_detectiveCooldownLeft -= Time.Delta;
			if ( _tirCooldownLeft > 0f )
				_tirCooldownLeft -= Time.Delta;
			if ( _exhaustedAttackBlock > 0f )
				_exhaustedAttackBlock -= Time.Delta;

			// Таймаут стрика: если стрик активен но игрок давно ничего не сбивал —
			// сбрасываем. Условие на _lastTirHitTime > 0 чтобы не дёргать в первый
			// кадр после загрузки.
			if ( TirCurrentStreak > 0
				&& _lastTirHitTime > 0f
				&& Time.Now - _lastTirHitTime > TirStreakTimeout )
			{
				TirCurrentStreak = 0;
			}

			if ( !IsDead && !IsSpectator
				&& GameManager.Instance?.CurrentState == GameManager.GameState.Playing )
				RoundSurvivedSeconds += Time.Delta;

			if ( !IsDead )
			{
				// Only fire a gun when slot 1 is the active inventory slot.
				var inv = Components.Get<PlayerInventory>();
				bool slot1Active = inv == null || inv.ActiveSlot == 1;

				// Murderer damage is handled by KnifeWeapon. Gun fires for Detective / armed Innocent.
				bool canShoot =
					slot1Active &&
					Role != PlayerRole.Murderer &&
					( Role == PlayerRole.Detective || ( Role == PlayerRole.Innocent && HasWeapon ) );

				if ( Input.Pressed( "attack1" ) && canShoot )
					Attack();

				// ADS: только пистолет (Detective или armed Innocent), пока зажат attack2.
				bool wantAds = canShoot && Role != PlayerRole.Murderer && Input.Down( "attack2" );
				if ( IsAds != wantAds ) IsAds = wantAds;
			}

			// FOV-зум для локального игрока. Sandbox.PlayerController по умолчанию
			// пишет в Scene.Camera FOV из пользовательских настроек каждый кадр
			// (UseFovFromPreferences=true). Это перезатирает нашу установку,
			// поэтому отключаем флаг разово и берём FOV под свой контроль.
			var ctrl = Components.Get<Sandbox.PlayerController>();
			if ( ctrl != null )
			{
				try { if ( ctrl.UseFovFromPreferences ) ctrl.UseFovFromPreferences = false; }
				catch { /* свойство может отсутствовать в др. версиях движка */ }
			}

			var cam = Scene.Camera;
			if ( cam != null )
			{
				if ( _baseFov < 0f ) _baseFov = cam.FieldOfView;
				float fovTarget = IsAds ? System.Math.Max( 20f, _baseFov - AdsFovZoom ) : _baseFov;
				float lerpT = System.Math.Clamp( AdsFovLerpSpeed * Time.Delta, 0f, 1f );
				cam.FieldOfView = MathX.Lerp( cam.FieldOfView, fovTarget, lerpT );
			}

			// Стамина (#7).
			if ( !IsDead && !IsSpectator )
				UpdateStamina( ctrl );
		}

		UpdateRendererVisibility();
		UpdateWeaponVisuals();
	}

	private void UpdateRendererVisibility()
	{
		// Alive: visible. Dead / spectator: hidden — the RagdollBody is the corpse visible to all.
		bool visible = !IsDead && !IsSpectator;
		SetRenderersEnabled( visible );
	}

	private void UpdateWeaponVisuals()
	{
		var gm = GameManager.Instance;
		bool isPlaying = gm != null && gm.CurrentState == GameManager.GameState.Playing;

		if ( MurdererWeaponVisual != null )
			MurdererWeaponVisual.Enabled = isPlaying && Role == PlayerRole.Murderer && !IsDead;

		if ( InnocentWeaponVisual != null )
			InnocentWeaponVisual.Enabled = HasWeapon && !IsDead;
	}

	private void UpdateStamina( Sandbox.PlayerController ctrl )
	{
		float maxStam = CurrentMaxStamina;
		if ( _staminaCurrent < 0f ) _staminaCurrent = maxStam;

		bool inHub = CurrentRoomId == System.Guid.Empty;
		if ( inHub )
		{
			_staminaCurrent = maxStam;
			_staminaExhausted = false;
			_lastWishDir = Vector3.Zero;
			return;
		}

		if ( ctrl == null ) return;

		// ── Прыжки тратят стамину ──────────────────────────────────────────
		// Списываем стамину только если игрок реально стоит на земле — иначе
		// повторные нажатия пробела в воздухе не дают прыжка, но раньше всё равно
		// съедали стамину.
		bool canJump = false;
		try { canJump = ctrl.IsOnGround; } catch { canJump = true; }
		if ( Input.Pressed( "jump" ) && !_staminaExhausted && canJump )
		{
			_staminaCurrent -= maxStam * JumpStaminaCost;
			if ( _staminaCurrent <= 0f )
			{
				_staminaCurrent = 0f;
				_staminaExhausted = true;
				if ( Role == PlayerRole.Murderer )
					_exhaustedAttackBlock = ExhaustedAttackDelay;
			}
		}

		// ── Дрейн/регенерация стамины ──────────────────────────────────────
		// Стамина тратится только если игрок РЕАЛЬНО бежит — Shift зажат +
		// есть инпут движения (WASD). Стоя на месте с зажатым Shift'ом стамина
		// не должна сливаться, иначе игроку приходится «выщёлкивать» Shift.
		bool wantsSprint = Input.Down( "run" ) && Input.AnalogMove.LengthSquared > 0.01f;
		bool isSprinting = wantsSprint && !_staminaExhausted;

		// Таймер «не спринтую» — растёт пока shift не зажат, обнуляется на спринте.
		// Ходьба не сбрасывает таймер — она тоже даёт регену работать.
		if ( isSprinting ) _notSprintingFor  = 0f;
		else               _notSprintingFor += Time.Delta;

		if ( isSprinting )
		{
			_staminaCurrent -= Time.Delta;
			if ( _staminaCurrent <= 0f )
			{
				_staminaCurrent = 0f;
				_staminaExhausted = true;
				// Убийца при истощении теряет право атаки на короткое время.
				if ( Role == PlayerRole.Murderer )
					_exhaustedAttackBlock = ExhaustedAttackDelay;
			}
		}
		else if ( canJump && _notSprintingFor >= StaminaRegenDelay )
		{
			// Регенерация запускается через StaminaRegenDelay после конца спринта.
			// Работает и при ходьбе, и стоя на месте. В воздухе нет (canJump =
			// IsOnGround) — антиабуз прыжка с shift.
			_staminaCurrent += Time.Delta * StaminaRegenRate;
			if ( _staminaCurrent >= maxStam ) _staminaCurrent = maxStam;
			if ( _staminaExhausted && _staminaCurrent >= StaminaRegenThreshold )
				_staminaExhausted = false;
		}

		// Жёстко переключаем RunSpeed контроллера: при истощении бег = ходьба,
		// иначе возвращаем исходное значение из префаба. Нужно, потому что
		// Sandbox.PlayerController сам считает WishVelocity по Input.Down("run"),
		// и наш кламп WishVelocity в OnUpdate может затираться порядком апдейтов.
		try
		{
			if ( _originalRunSpeed < 0f ) _originalRunSpeed = ctrl.RunSpeed;
			float targetRun = _staminaExhausted ? ctrl.WalkSpeed : _originalRunSpeed;
			if ( System.Math.Abs( ctrl.RunSpeed - targetRun ) > 0.01f )
				ctrl.RunSpeed = targetRun;
		}
		catch { }

		ApplyStaminaSpeedCap( ctrl );
	}

	// Лимит скорости + инерция поворота. Вызывается из OnUpdate и OnFixedUpdate
	// (двойное применение покрывает неопределённый порядок компонентов).
	private void ApplyStaminaSpeedCap( Sandbox.PlayerController ctrl )
	{
		float norm = StaminaNormalized;
		bool needsCap = _staminaExhausted || norm < 1f;

		// ── Инерция поворота ──────────────────────────────────────────────
		// Сравниваем текущее желаемое направление с прошлым.
		// dot 1 = прямо, 0 = 90°, -1 = разворот. turnFactor → 0..1.
		float turnFactor = 1f;
		Vector3 wishNow = Vector3.Zero;
		try
		{
			wishNow = ctrl.WishVelocity;
			if ( wishNow.LengthSquared > 100f && !_lastWishDir.IsNearZeroLength )
			{
				float dot = Vector3.Dot( wishNow.Normal, _lastWishDir );
				turnFactor = System.Math.Clamp( (dot + 1f) * 0.5f, 0f, 1f );
				// Смягчаем кривую: корень делает небольшие повороты почти незаметными,
				// но резкий разворот на 180° всё равно значительно гасит скорость.
				turnFactor = (float)System.Math.Sqrt( (double)turnFactor );
			}
			if ( wishNow.LengthSquared > 100f )
				_lastWishDir = wishNow.Normal;
		}
		catch { }

		bool hasTurnPenalty = turnFactor < 0.95f;
		if ( !needsCap && !hasTurnPenalty ) return;

		// ── Расчёт максимальной скорости ─────────────────────────────────
		float sprintRef  = StaminaWalkSpeed * CurrentSprintMult;
		float slowdownT  = System.Math.Clamp( norm / 0.8f, 0f, 1f );
		float baseMax = _staminaExhausted
			? StaminaWalkSpeed
			: MathX.Lerp( StaminaWalkSpeed, sprintRef, slowdownT );

		// Поворот дополнительно замедляет: при развороте на 180° — до уровня ходьбы.
		float finalMax = MathX.Lerp( StaminaWalkSpeed, baseMax, turnFactor );

		try
		{
			float lenSq = wishNow.LengthSquared;
			if ( lenSq > finalMax * finalMax )
				ctrl.WishVelocity = wishNow.Normal * finalMax;
		}
		catch { }
	}

	// Счётчик для дроссел-опроса pending cloud save — проверяем раз в ~секунду,
	// а не каждый кадр (50 Гц). При 64 игроках это снижает lock-нагрузку
	// с 3200/сек до ~64/сек. Throttle всё равно 12с, потеря точности не важна.
	private int _cloudCheckCounter;

	// Кешированное последнее применённое значение EquippedCosmeticsSerialized.
	// Используется в OnUpdate чтобы понять, изменилась ли экипировка
	// (особенно для прокси-игроков, которым [Sync] приносит новое значение).
	private string _lastAppliedEquippedClothing = null;

	protected override void OnFixedUpdate()
	{
		// Throttled cloud save: внутри StatsPersistence стоит ограничение
		// «не чаще раз в ~12 секунд (+ jitter)» — здесь мы опрашиваем pending
		// раз в ~50 тиков (≈1 сек). POST уходит только когда таймер истёк.
		// Только owner отправляет — прокси не держат собственного pending'a.
		if ( !IsProxy && ++_cloudCheckCounter >= 50 )
		{
			_cloudCheckCounter = 0;
			StatsPersistence.TryFlushCloudSave();
		}

		// Дублируем ограничение скорости в FixedUpdate для надёжности.
		if ( IsProxy || IsDead || IsSpectator ) return;
		if ( CurrentRoomId == System.Guid.Empty ) return;
		if ( StaminaNormalized >= 1f && !_staminaExhausted ) return;

		var ctrl = Components.Get<Sandbox.PlayerController>();
		if ( ctrl == null ) return;
		ApplyStaminaSpeedCap( ctrl );
	}

	protected override void OnDestroy()
	{
		// Финальный flush на выходе из игры — чтобы pending изменения за
		// последние 12 секунд не потерялись. Fire-and-forget: HTTP-запрос
		// уйдёт в фоне, обычно успевает при штатном disconnect. При Alt+F4
		// процесс может оборваться раньше — но локальный stats.json уже
		// синхронно сохранён, и при следующем входе ApplyCloudEntry загрузит
		// либо облако (если успело), либо локалку (которая свежая).
		if ( !IsProxy )
		{
			try { _ = StatsPersistence.FlushCloudSaveAsync(); } catch { }
		}
	}

	private void InitTutorial( string steamId )
	{
		if ( string.IsNullOrEmpty( steamId ) ) return;
		var entry = StatsPersistence.GetFor( steamId );
		TutorialVisible = !entry.HasSeenTutorial;
	}

	public void DismissTutorial()
	{
		TutorialVisible = false;
		var sid = GetSteamIdString();
		if ( string.IsNullOrEmpty( sid ) ) return;
		var entry = StatsPersistence.GetFor( sid );
		entry.HasSeenTutorial = true;
		StatsPersistence.Save();
		StatsPersistence.CloudSync( sid );
		try { Sandbox.Services.Achievements.Unlock( "mm_tutorial_done" ); } catch { }
	}

	/// <summary>
	/// Загружает данные из облачного Kv и применяет их поверх локального кэша.
	/// Облако — авторитетный источник: если игрок играл на другом ПК, его
	/// кристаллы/косметика придут отсюда. Запускается один раз при спавне.
	/// </summary>
	private async System.Threading.Tasks.Task SyncFromCloudAsync()
	{
		var sid = GetSteamIdString();
		if ( string.IsNullOrEmpty( sid ) ) return;

		var cloudEntry = await StatsPersistence.LoadFromCloudAsync();

		// Если компонент уничтожен пока шёл запрос — выходим.
		if ( !IsValid || IsProxy ) return;

		if ( cloudEntry == null )
		{
			// Данных в облаке нет — первый вход или Kv пуст.
			// Мигрируем локальные данные в облако чтобы при следующем заходе
			// (с другого ПК или после переустановки) они были доступны.
			var localEntry = StatsPersistence.GetFor( sid );
			await StatsPersistence.SaveToCloudAsync( localEntry );
			Log.Info( "[Cloud] First login — local data uploaded to cloud." );
			return;
		}

		// Применяем облачные данные (перезаписывают локальный кэш).
		StatsPersistence.ApplyCloudEntry( this, cloudEntry, sid );

		// Обновляем внешний вид если косметика изменилась на другом устройстве.
		ApplyClothing();

		Log.Info( "[Cloud] Inventory synced from cloud." );
	}

	// Ручное открытие туториала (команда /guide в чате).
	public void ShowTutorial() => TutorialVisible = true;

	/// <summary>
	/// Host-authoritative role assignment. Broadcast relays the host's decision to
	/// every client so the owning player updates their [Sync]'d Role.
	/// Cheat check: we log (and reject locally on host) any non-host caller, which is
	/// a defense-in-depth hint — for full prevention use server-owned state.
	/// </summary>
	/// <summary>
	/// Игрок просит войти в конкретную комнату (из меню портала).
	/// Обрабатывается только на хосте.
	/// </summary>
	[Rpc.Broadcast]
	public void RequestRoomEntry( System.Guid roomId, bool asSpectator )
	{
		if ( !Networking.IsHost ) return;

		var room = Scene.GetAllComponents<GameRoom>()
			.FirstOrDefault( r => r.GameObject.Id == roomId );
		if ( room == null ) return;

		// Серверная защита: войти НЕ как спектатор можно только если раунд ещё не идёт.
		// Иначе клиент мог бы заспавниться мирным посреди игры (даже если UI скрывает кнопку).
		bool roundActive = room.State == GameRoom.GameState.Playing
		                || room.State == GameRoom.GameState.StartingRound;
		bool effectiveSpectator = asSpectator || roundActive;

		SetSpectator( effectiveSpectator );
		SetCurrentRoom( roomId );

		var entry = room.LobbyPoint?.WorldPosition;
		if ( entry.HasValue ) TeleportTo( entry.Value );
	}

	/// <summary>
	/// Игрок просит вернуться в хаб (например через /hub команду в чате).
	/// RPC летит на ВСЕ клиенты, обрабатываем только на хосте.
	/// </summary>
	[Rpc.Broadcast]
	public void RequestReturnToHub()
	{
		if ( !Networking.IsHost ) return;
		GameManager.ReturnPlayerToHub( this );
	}

	/// <summary>
	/// Игрок просит зайти в тир. Зовётся клиентом из TirEntryPortal —
	/// хост телепортирует и выдаёт оружие.
	/// </summary>
	[Rpc.Broadcast]
	public void RequestTirEntry()
	{
		if ( !Networking.IsHost ) return;
		var tir = TirZone.Instance;
		if ( tir == null || tir.SpawnPoint == null ) return;

		SetCurrentRoom( tir.GameObject.Id );
		TeleportTo( tir.SpawnPoint.WorldPosition );
		if ( tir.GiveKnifeOnEntry ) GrantWeapon();
	}

	/// <summary>
	/// Уведомить хост о попадании в мишень тира. RPC идёт от PlayerStats
	/// (которым владеет caller) → надёжно дойдёт до хоста для любого
	/// клиента включая бота. Хост валидирует и применяет hit на мишени.
	/// </summary>
	[Rpc.Broadcast]
	public void NotifyTirHit( System.Guid targetGuid )
	{
		if ( !Networking.IsHost ) return;
		// Антибуст: вызвать может только сам игрок-владелец PlayerStats.
		var caller = Rpc.Caller;
		if ( caller != null && Network.Owner != null && caller != Network.Owner ) return;

		var target = Scene.GetAllComponents<TirTarget>()
			.FirstOrDefault( t => t != null && t.GameObject != null
				&& t.GameObject.Id == targetGuid );
		if ( target == null ) return;
		target.HostApplyShot( this );
	}

	/// <summary>
	/// Игрок просит выйти из тира. Зовётся клиентом из TirReturnPortal —
	/// хост телепортирует в хаб и забирает оружие.
	/// </summary>
	[Rpc.Broadcast]
	public void RequestTirExit()
	{
		if ( !Networking.IsHost ) return;
		var returnPoint = TirZone.Instance?.HubReturnPoint?.WorldPosition
			?? GameManager.Instance?.HubSpawnPoint?.WorldPosition;

		SetCurrentRoom( System.Guid.Empty );
		if ( returnPoint.HasValue )
			TeleportTo( returnPoint.Value );
		if ( HasWeapon ) TakeWeaponAway();
	}

	/// <summary>
	/// Хост-only: назначить игрока на комнату. Owner обновит CurrentRoomId у себя
	/// (это [Sync] поле, так что Sync пробросит на остальных).
	/// </summary>
	[Rpc.Broadcast]
	public void SetCurrentRoom( System.Guid roomId )
	{
		var caller = Rpc.Caller;
		if ( caller != null && !caller.IsHost )
		{
			Log.Warning( $"[ANTICHEAT] SetCurrentRoom rejected: non-host caller." );
			return;
		}
		if ( !IsProxy ) CurrentRoomId = roomId;
	}

	/// <summary>
	/// Хост-only: телепортировать игрока в указанную точку. Owner у себя
	/// двигает свой transform.
	/// </summary>
	[Rpc.Broadcast]
	public void TeleportTo( Vector3 pos )
	{
		var caller = Rpc.Caller;
		if ( caller != null && !caller.IsHost )
		{
			Log.Warning( $"[ANTICHEAT] TeleportTo rejected: non-host caller." );
			return;
		}
		if ( !IsProxy ) GameObject.WorldPosition = pos;
	}

	[Rpc.Broadcast]
	public void AssignRole( PlayerRole role )
	{
		// Reject if this call came from a non-host connection.
		var caller = Rpc.Caller;
		if ( caller != null && !caller.IsHost )
		{
			Log.Warning( $"[ANTICHEAT] AssignRole rejected: caller '{caller.DisplayName}' is not host." );
			return;
		}

		if ( !IsProxy )
		{
			Role = role;
		}
	}

	/// <summary>
	/// Host-only: toggle spectator mode for this player.
	/// </summary>
	[Rpc.Broadcast]
	public void SetSpectator( bool spec )
	{
		var caller = Rpc.Caller;
		if ( caller != null && !caller.IsHost )
		{
			Log.Warning( $"[ANTICHEAT] SetSpectator rejected: caller '{caller.DisplayName}' is not host." );
			return;
		}
		if ( !IsProxy )
			IsSpectator = spec;
	}

	[Rpc.Broadcast]
	public void CollectCoin()
	{
		if ( !IsProxy )
		{
			Coins += 1;
			RoundCoinsPickedUp += 1;
		}
	}

	/// <summary>
	/// Хост-only: отправляет owner'у разбивку наград за раунд для отображения в RoundEndUI.
	/// Параметры — кристаллы по категориям; UI сложит и покажет breakdown с нулями.
	/// </summary>
	[Rpc.Broadcast]
	public void SetRoundRewards( int match, int aliveTime, int coins, int killMurd, int teamWin, int kills, int murdPerf )
	{
		var caller = Rpc.Caller;
		if ( caller != null && !caller.IsHost )
		{
			Log.Warning( $"[ANTICHEAT] SetRoundRewards rejected: non-host caller." );
			return;
		}
		if ( IsProxy ) return;
		RewardMatchComplete   = match;
		RewardAliveTime       = aliveTime;
		RewardCoins           = coins;
		RewardMurdererKilled  = killMurd;
		RewardTeamWin         = teamWin;
		RewardKills           = kills;
		RewardMurdererPerfect = murdPerf;
	}

	[Rpc.Broadcast]
	public void SpendCoins( int amount )
	{
		var caller = Rpc.Caller;
		if ( caller != null && !caller.IsHost )
		{
			Log.Warning( $"[ANTICHEAT] SpendCoins rejected: non-host caller." );
			return;
		}
		if ( !IsProxy )
			Coins = System.Math.Max( 0, Coins - amount );
	}

	[Rpc.Broadcast]
	public void IncrementStat( string statName, int delta )
	{
		// Разрешаем вызов либо хосту, либо самому владельцу этих стат —
		// иначе при не-хост игроке убийства из KnifeWeapon/Attack отклонялись
		// и RoundKills не рос, а значит мардер не получал свои +12 за убийство
		// в финальной сводке наград.
		var caller = Rpc.Caller;
		bool isHost = caller == null || caller.IsHost;
		bool isOwner = caller != null && Network.Owner != null && caller == Network.Owner;
		if ( !isHost && !isOwner ) return;
		if ( IsProxy ) return;
		switch ( statName )
		{
			case "wins":       StatWins += delta; break;
			case "kills":      StatKills += delta; RoundKills += delta; break;
			case "survived":   StatSurvived += delta; break;
			case "detective":  StatRoundsAsDetective += delta; break;
			case "catches":    StatMurdererCatches += delta; break;
			case "tir_streak": /* StatTirBestStreak уже выставлен в Attack(); тут только пуш в облако */ break;
		}

		// Сохраняем локально (Variant 3: персист на каждом клиенте у себя).
		StatsPersistence.StoreFrom( this, GetSteamIdString() );

		// Дополнительно пушим в Sandbox.Services.Stats — глобальный лидерборд.
		// Доступен даже офлайн-игрокам (Steam-инфраструктура).
		PushLeaderboardStat( statName, delta );

		// Event-based ачивка: 3 убийства за раунд (мардер).
		if ( statName == "kills" && RoundKills >= 3 )
			try { Sandbox.Services.Achievements.Unlock( "mm_triple_kill" ); } catch { }
	}

	/// <summary>
	/// Пуш инкремента в Sandbox.Services.Stats. После успешного пуша обновляет
	/// PushedXxx в StatEntry, чтобы SyncLocalStatsToLeaderboard знал реальный
	/// баланс и не двоил данные при следующем заходе.
	/// </summary>
	private void PushLeaderboardStat( string statName, int delta )
	{
		try
		{
			Sandbox.Services.Stats.Increment( "mm_" + statName, delta );
			// Пуш прошёл — фиксируем в персисте.
			var sid = GetSteamIdString();
			if ( !string.IsNullOrEmpty( sid ) )
			{
				var entry = StatsPersistence.GetFor( sid );
				switch ( statName )
				{
					case "wins":       entry.PushedWins       += delta; break;
					case "kills":      entry.PushedKills      += delta; break;
					case "survived":   entry.PushedSurvived   += delta; break;
					case "detective":  entry.PushedDetective  += delta; break;
					case "catches":    entry.PushedCatches    += delta; break;
					case "tir_streak": entry.PushedTirStreak  += delta; break;
				}
				StatsPersistence.Save();
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"[Leaderboard] push failed ({statName}): {e.Message}" );
		}
	}

	/// <summary>
	/// При загрузке пушит разницу (local − pushed) в Sandbox.Services через Increment.
	/// Это выравнивает расхождение (#19/#26) когда предыдущие пуши были прерваны.
	/// Sandbox.Services.Stats не имеет метода Set, поэтому используем delta-подход:
	/// храним «сколько уже запушено» в StatEntry, пушим только недостающее.
	/// </summary>
	private void SyncLocalStatsToLeaderboard()
	{
		var sid = GetSteamIdString();
		if ( string.IsNullOrEmpty( sid ) ) return;
		var entry = StatsPersistence.GetFor( sid );
		bool changed = false;

		// Свойства нельзя передать по ref — копируем в локальные переменные.
		int pushedWins      = entry.PushedWins;
		int pushedKills     = entry.PushedKills;
		int pushedSurvived  = entry.PushedSurvived;
		int pushedDetective = entry.PushedDetective;
		int pushedCatches   = entry.PushedCatches;
		int pushedTirStreak = entry.PushedTirStreak;

		void PushDelta( string key, int local, ref int pushed )
		{
			int delta = local - pushed;
			if ( delta <= 0 ) return;
			try
			{
				Sandbox.Services.Stats.Increment( "mm_" + key, delta );
				pushed  += delta;
				changed  = true;
			}
			catch { }
		}

		PushDelta( "wins",       StatWins,              ref pushedWins );
		PushDelta( "kills",      StatKills,             ref pushedKills );
		PushDelta( "survived",   StatSurvived,          ref pushedSurvived );
		PushDelta( "detective",  StatRoundsAsDetective, ref pushedDetective );
		PushDelta( "catches",    StatMurdererCatches,   ref pushedCatches );
		PushDelta( "tir_streak", StatTirBestStreak,     ref pushedTirStreak );

		if ( changed )
		{
			entry.PushedWins       = pushedWins;
			entry.PushedKills      = pushedKills;
			entry.PushedSurvived   = pushedSurvived;
			entry.PushedDetective  = pushedDetective;
			entry.PushedCatches    = pushedCatches;
			entry.PushedTirStreak  = pushedTirStreak;
			StatsPersistence.Save();
		}
	}

	[Rpc.Broadcast]
	public void GrantWeapon()
	{
		var caller = Rpc.Caller;
		if ( caller != null && !caller.IsHost )
		{
			Log.Warning( $"[ANTICHEAT] GrantWeapon rejected: non-host caller." );
			return;
		}
		if ( !IsProxy )
			HasWeapon = true;
	}

	[Rpc.Broadcast]
	public void ResetForNewRound()
	{
		var caller = Rpc.Caller;
		if ( caller != null && !caller.IsHost ) return;

		if ( !IsProxy )
		{
			Role = PlayerRole.Innocent;
			Coins = 0;
			HasWeapon = false;
			IsDead = false;
			IsSpectator = false;
			_killGuard = false;
			_murdererCooldownLeft = 0f;
			_detectiveCooldownLeft = 0f;
			_staminaCurrent = -1f; // пересчитается под новую роль при старте раунда
			_staminaExhausted = false;
			_notSprintingFor = 0f;
			_exhaustedAttackBlock = 0f;
			_lastWishDir = Vector3.Zero;
			RoundKills = 0;
			RoundSurvivedSeconds = 0f;
			RoundCoinsPickedUp = 0;
			RoundMurdererKills = 0;
			RewardMatchComplete   = 0;
			RewardAliveTime       = 0;
			RewardCoins           = 0;
			RewardMurdererKilled  = 0;
			RewardTeamWin         = 0;
			RewardKills           = 0;
			RewardMurdererPerfect = 0;
		}

		// На ВСЕХ клиентах — размораживаем контроль и физику.
		var pc = Components.Get<Sandbox.PlayerController>();
		if ( pc != null )
		{
			pc.Enabled = true;            // на случай если кто-то всё-таки выключал Enabled
			try { pc.UseInputControls = true; } catch { }
		}

		var rb = Components.Get<Rigidbody>();
		if ( rb != null ) rb.Gravity = true;

		// Принудительно включаем рендереры на ВСЕХ клиентах. UpdateRendererVisibility
		// ловится в OnUpdate по IsDead, но из-за [Sync]-задержки proxy может на пару кадров
		// видеть устаревшее IsDead=true → модель остаётся невидимой. Форс-включаем здесь.
		// Сбрасываем _lastVisibleState чтобы SetRenderersEnabled гарантированно прошёлся.
		_lastVisibleState = null;
		SetRenderersEnabled( true );

		// Возвращаем коллайдеры — игрок снова может получать урон и упирается в стены.
		_lastCollidersEnabled = null;
		SetCollidersEnabled( true );
		_lastControllerEnabled = pc != null ? (bool?)true : null;
	}

	/// <summary>
	/// Находит ближайшую (по дистанции от стрелка) живую TirTarget'у вдоль луча.
	/// Использует ray-vs-sphere через проекцию центра мишени на луч — независимо
	/// от того, trigger у мишени коллайдер или нет. Радиус "зоны попадания"
	/// берётся из bounding box рендера мишени (либо 30 юнитов по умолчанию).
	/// </summary>
	private TirTarget FindTirTargetAlongRay( Vector3 origin, Vector3 dir, float maxDist )
	{
		TirTarget best = null;
		float bestDist = maxDist;
		var tirRoom = TirZone.TirRoomId;
		foreach ( var t in Scene.GetAllComponents<TirTarget>() )
		{
			if ( t == null || !t.IsValid ) continue;
			if ( t.Hidden ) continue;
			if ( t.RoomId != tirRoom ) continue;
			// Per-player мишень: попадаем только в свои клоны.
			if ( !t.IsOwnedBy( this ) ) continue;

			// Зона попадания — берём из настоящего коллайдера, чтобы тонкая
			// штанга в воздухе не считалась хитом. Приоритет:
			//   1) SphereCollider — точно центр + радиус.
			//   2) BoxCollider    — центр + половина диагонали (вписанная сфера).
			//   3) Bounds рендера — как fallback.
			Vector3 center;
			float radius;
			var sphere = t.Components.Get<Sandbox.SphereCollider>( FindMode.EverythingInSelfAndDescendants );
			var box    = sphere == null
				? t.Components.Get<Sandbox.BoxCollider>( FindMode.EverythingInSelfAndDescendants )
				: null;
			if ( sphere != null )
			{
				var goSph = sphere.GameObject;
				center = goSph.WorldPosition + goSph.WorldRotation * sphere.Center;
				var ws  = goSph.WorldScale;
				float uniform = System.Math.Max( ws.x, System.Math.Max( ws.y, ws.z ) );
				radius = sphere.Radius * uniform;
			}
			else if ( box != null )
			{
				var goBox = box.GameObject;
				center = goBox.WorldPosition + goBox.WorldRotation * box.Center;
				// Полуразмер box * scale, далее берём длину для радиуса описанной сферы.
				var sz = box.Scale * goBox.WorldScale;
				radius = sz.Length * 0.5f;
			}
			else
			{
				var mr = t.Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndDescendants );
				if ( mr != null && mr.Bounds.Size.Length > 1f )
				{
					center = mr.Bounds.Center;
					radius = mr.Bounds.Size.Length * 0.5f;
				}
				else
				{
					center = t.GameObject.WorldPosition;
					radius = 30f;
				}
			}

			Vector3 toCenter = center - origin;
			float along = Vector3.Dot( toCenter, dir );
			if ( along < 0f || along > bestDist ) continue;

			Vector3 closest = origin + dir * along;
			float sideways = Vector3.DistanceBetween( closest, center );
			if ( sideways > radius ) continue;

			best = t;
			bestDist = along;
		}
		return best;
	}

	private void Attack()
	{
		// Если игрок в тир-зоне — упрощённая ветка стрельбы: оружие не отбирается,
		// кулдаун 1с (TirShotCooldown), цель = TirTarget а не PlayerStats. Никакая
		// логика раунда (роли, kill, win-condition) не задевается.
		bool inTir = TirZone.TirRoomId != System.Guid.Empty
			&& CurrentRoomId == TirZone.TirRoomId;
		if ( inTir )
		{
			if ( _tirCooldownLeft > 0f ) return;
			_tirCooldownLeft = TirShotCooldown;
			TriggerPistolFire();

			var camTir = Scene.Camera;
			if ( camTir == null ) return;
			var rayTir = camTir.ScreenNormalToRay( 0.5f );

			// Дальность в тире — 2× от обычной (4000 vs 2000), чтоб с дальних
			// дистанций тоже стреляло по мишеням на стрелковом стенде.
			const float TIR_RANGE = 4000f;

			// Сначала обычный трассер для эффекта (попадание в стену/пол).
			// Фильтруем оба написания тега (с и без подчёркивания) — стандарт s&box
			// "player_clip", но люди часто пишут "playerclip" и забывают.
			var trTir = Scene.Trace.Ray( rayTir, TIR_RANGE )
				.IgnoreGameObjectHierarchy( GameObject )
				.WithoutTags( "trigger", "player_clip", "playerclip" )
				.Run();
			Vector3 endTir = trTir.Hit ? trTir.HitPosition : rayTir.Project( TIR_RANGE );
			SpawnTracer( rayTir.Position, endTir );

			// Поиск мишени НЕ через collider-trace (триггер-коллайдеры в s&box
			// часто пропускают луч). Идём по списку всех TirTarget'ов и ищем
			// ближайшую мишень в конусе луча. Радиус «зоны попадания» — половина
			// диагонали bounding box модели или фиксированные ~30 юнитов.
			var hitTarget = FindTirTargetAlongRay( rayTir.Position, rayTir.Forward, TIR_RANGE );
			if ( hitTarget != null )
			{
				NotifyTirHit( hitTarget.GameObject.Id );
				if ( !IsProxy )
				{
					TirHits++;
					// Стрик: если предыдущий хит был в окне TirStreakTimeout — продолжаем,
					// иначе начинаем новый стрик с 1.
					if ( TirCurrentStreak > 0
						&& Time.Now - _lastTirHitTime <= TirStreakTimeout )
					{
						TirCurrentStreak++;
					}
					else
					{
						TirCurrentStreak = 1;
					}
					_lastTirHitTime = Time.Now;

					// Если перебили лучший — пушим в глобальный лидерборд через
					// дельта-инкремент. Sandbox.Services не имеет Set, но
					// (newBest - oldBest) при последовательных улучшениях
					// в сумме даёт newBest.
					if ( TirCurrentStreak > StatTirBestStreak )
					{
						int delta = TirCurrentStreak - StatTirBestStreak;
						StatTirBestStreak = TirCurrentStreak;
						IncrementStat( "tir_streak", delta );
					}
				}
			}
			else
			{
				// Промах — мгновенно сбрасываем стрик.
				if ( !IsProxy ) TirCurrentStreak = 0;
			}
			return;
		}

		// Gun attack only (Detective / armed Innocent). Murderer handled by KnifeWeapon.
		if ( Role == PlayerRole.Detective && _detectiveCooldownLeft > 0f )
			return;

		var cam = Scene.Camera;
		if ( cam == null ) return;

		// Триггерим анимацию выстрела на активной вьюмодели пистолета.
		TriggerPistolFire();

		var ray = cam.ScreenNormalToRay( 0.5f );
		var tr = Scene.Trace.Ray( ray, 2000f )
			.UseHitboxes()
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		Vector3 endPoint = tr.Hit ? tr.HitPosition : ray.Project( 2000f );
		SpawnTracer( ray.Position, endPoint );

		var target = tr.Hit ? tr.GameObject?.Root?.GetComponent<PlayerStats>() : null;
		bool validHit = target != null && !target.IsDead;

		if ( Role == PlayerRole.Detective )
		{
			// Detective keeps the gun. Cooldown after ANY shot (DetectiveShotCooldown).
			_detectiveCooldownLeft = DetectiveShotCooldown;

			if ( !validHit ) return;

			if ( target.Role == PlayerRole.Murderer )
			{
				// Отправляем IncrementRoundMurdKills ДО Kill — хост обработает
				// счётчик до запуска CheckWinConditions → AwardStats.
				IncrementRoundMurdKills();
				target.Kill();
				IncrementStat( "kills", 1 );
				IncrementStat( "catches", 1 );
			}
			else
			{
				// Shot an innocent: both die.
				target.Kill();
				Kill();
			}
			return;
		}

		// Innocent with legacy pickup weapon — single shot, weapon is consumed.
		if ( Role == PlayerRole.Innocent )
		{
			TakeWeaponAway();

			if ( !validHit ) return;

			if ( target.Role == PlayerRole.Murderer )
			{
				IncrementRoundMurdKills();
				target.Kill();
				IncrementStat( "kills", 1 );
				IncrementStat( "catches", 1 );
			}
			else
			{
				target.Kill();
				Kill();
			}
		}
	}

	[Rpc.Broadcast]
	public void TakeWeaponAway()
	{
		if ( !IsProxy )
			HasWeapon = false;
	}

	/// <summary>
	/// Инкрементирует RoundMurdererKills через RPC — гарантирует что хост получит
	/// значение ДО того, как обработает Kill() и запустит AwardStats. Только для
	/// Innocent/Detective при убийстве мардера. Не обновляет глобальный лидерборд.
	/// </summary>
	[Rpc.Broadcast]
	public void IncrementRoundMurdKills()
	{
		var caller = Rpc.Caller;
		bool isHost  = caller == null || caller.IsHost;
		bool isOwner = caller != null && Network.Owner != null && caller == Network.Owner;
		if ( !isHost && !isOwner ) return;
		if ( IsProxy ) return;
		RoundMurdererKills++;
	}

	[Rpc.Broadcast]
	public void Kill()
	{
		// Локальный гард срабатывает раньше [Sync]-поля IsDead на прокси — предотвращает
		// двойное воспроизведение звука смерти при повторном RPC до синхронизации.
		if ( _killGuard ) return;

		// Гард тира: в зоне ожидания убивать нельзя ничем. Раньше _killGuard
		// идёт, потому что мы хотим полностью игнорировать call, не запоминая
		// «уже умирал». Покрывает все источники: пистолет, нож, дебаг-команды.
		if ( TirZone.TirRoomId != System.Guid.Empty && CurrentRoomId == TirZone.TirRoomId )
			return;

		_killGuard = true;

		if ( IsDead ) return;

		bool wasDetective = Role == PlayerRole.Detective;
		Vector3 deathPos = GameObject.WorldPosition;
		Rotation deathRot = GameObject.WorldRotation;

		if ( !IsProxy )
			IsDead = true;

		// Звук смерти — слышат все.
		if ( !string.IsNullOrEmpty( DeathSound ) )
			Sfx.Play( DeathSound, deathPos );

		FreezeDeadCharacter();

		Log.Info( $"{GameObject.Name} убит!" );

		// Host authoritatively spawns the corpse + any role-specific drops.
		if ( Networking.IsHost )
		{
			SpawnRagdoll( deathPos, deathRot );
			if ( wasDetective )
				OnDetectiveDied( deathPos );
		}
	}

	private void FreezeDeadCharacter()
	{
		// Сбрасываем ADS при смерти — иначе FOV остаётся прицельным в режиме спектатора.
		IsAds = false;

		// Отключаем PlayerController полностью — это удаляет его физическую капсулу,
		// которая иначе остаётся как невидимый хитбокс и блокирует пули/проходы.
		// ResetForNewRound снова включает его через pc.Enabled = true.
		var pc = Components.Get<Sandbox.PlayerController>();
		if ( pc != null )
		{
			try { pc.Enabled = false; } catch { }
			_lastControllerEnabled = false;
		}

		var rb = Components.Get<Rigidbody>();
		if ( rb != null )
		{
			rb.Velocity = Vector3.Zero;
			rb.AngularVelocity = Vector3.Zero;
		}

		// Снимаем хитбоксы и коллайдеры — иначе труп продолжает ловить пули
		// и физически блокировать живых игроков. Видимое тело (труп) — это
		// отдельный RagdollBody со своими коллайдерами.
		SetCollidersEnabled( false );

		// Останавливаем анимации — иначе SkinnedModelRenderer продолжит крутить
		// locomotion-цикл, и визуально тело выглядит как «живой двойник».
		foreach ( var smr in Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			try { smr.Set( "move_x", 0f ); smr.Set( "move_y", 0f ); smr.Set( "move_z", 0f ); } catch { }
		}
	}

	private void SpawnRagdoll( Vector3 pos, Rotation rot )
	{
		if ( RagdollPrefab == null )
		{
			Log.Warning( "[Ragdoll] RagdollPrefab не назначен на PlayerStats — труп не появится. " +
				"В инспекторе Player prefab перетащи сюда отдельный Ragdoll prefab (SkinnedModelRenderer + ModelPhysics + RagdollBody)." );
			return;
		}

		var body = RagdollPrefab.Clone( pos, rot );
		if ( body == null )
		{
			Log.Warning( "[Ragdoll] Clone вернул null — проверь что RagdollPrefab это валидный GameObject prefab." );
			return;
		}

		// Защита от классической ошибки: если в слот перетащили сам Player prefab, клон
		// окажется полноценным игроком с PlayerStats/PlayerController — именно этот баг
		// выглядит как «появляются отдельные плееры, повторяющие действия».
		if ( body.GetComponent<PlayerStats>() != null || body.GetComponent<Sandbox.PlayerController>() != null )
		{
			Log.Error( "[Ragdoll] RagdollPrefab содержит PlayerStats/PlayerController — похоже, ты перетащил Player prefab. " +
				"Нужен отдельный prefab только с SkinnedModelRenderer + ModelPhysics + RagdollBody." );
			body.Destroy();
			return;
		}

		// Нет физики — регдолл будет стоять столбом. Предупреждаем, но не отменяем.
		if ( body.Components.Get<ModelPhysics>( FindMode.EverythingInSelfAndDescendants ) == null )
		{
			Log.Warning( "[Ragdoll] На RagdollPrefab нет компонента ModelPhysics — тело не упадёт физически." );
		}

		var ragdoll = body.GetComponent<RagdollBody>();
		if ( ragdoll == null )
		{
			Log.Warning( "[Ragdoll] На RagdollPrefab нет компонента RagdollBody — имя/роль жертвы не сохранятся." );
		}
		else
		{
			ragdoll.VictimName = GameObject.Name;
			ragdoll.VictimRole = (int)Role;
			// Помечаем труп scope'ом текущей румы — клиенты в других румах
			// у себя выключат симуляцию физики и рендер.
			ragdoll.RoomId = CurrentRoomId;
		}

		body.NetworkSpawn();
	}

	private void OnDetectiveDied( Vector3 pos )
	{
		if ( DetectiveGunPickupPrefab != null )
		{
			var drop = DetectiveGunPickupPrefab.Clone( pos + Vector3.Up * 20f );

			// Скоупим дроп руме — клиенты в других румах его не рендерят.
			var pickupComp = drop.GetComponent<DetectiveGunPickup>();
			if ( pickupComp != null ) pickupComp.RoomId = CurrentRoomId;

			drop.NetworkSpawn();
		}
		else
		{
			Log.Warning( "DetectiveGunPickupPrefab not assigned on PlayerStats — no gun will drop." );
		}

		BroadcastDetectiveDiedMessage();
	}

	[Rpc.Broadcast]
	public static void BroadcastDetectiveDiedMessage()
	{
		Log.Info( "Detective has died! Find the weapon!" );
	}

	[Rpc.Broadcast]
	private void SpawnTracerBroadcast( Vector3 from, Vector3 to )
	{
		// Звук выстрела — слышат все клиенты.
		if ( !string.IsNullOrEmpty( GunShotSound ) )
			Sfx.Play( GunShotSound, from );

		if ( BulletTracerPrefab == null ) return;
		var midpoint = (from + to) * 0.5f;
		var tracer = BulletTracerPrefab.Clone( midpoint );
		var tc = tracer.GetComponent<BulletTracer>();
		if ( tc != null )
		{
			tc.StartPoint = from;
			tc.EndPoint = to;
		}
	}

	private void SpawnTracer( Vector3 from, Vector3 to )
	{
		SpawnTracerBroadcast( from, to );
	}

	private void TriggerPistolFire()
	{
		// Триггерим анимацию выстрела на citizen body — animgraph знает позу
		// Pistol через CitizenAnimationHelper и проиграет выстрел.
		var body = Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		if ( body == null ) return;
		try { body.Set( "b_attack", true ); } catch { }

		// FP-анимация отдачи пистолета: дёргаем АКТИВНУЮ модель пистолета (любой
		// скин — Pistol, pistol_2, gun_golden и т.д.). По префиксу "pistol"/"Pistol"
		// — case-insensitive, чтобы и старое имя "Pistol", и новые "pistol_*"
		// попадали под один code path.
		PunchActiveHeldItemByPrefix( "pistol", new Vector3( -3.5f, 0f, 1.2f ), new Angles( -8f, 0f, 0f ), 0.12f );
	}

	/// <summary>
	/// Находит дочерний GameObject heldItems по точному имени (например, "Pistol")
	/// и дёргает его FirstPersonHeldItemOffset для FP-визуальной отдачи/удара.
	/// Локальный визуал, прокси-игрокам не виден.
	/// </summary>
	public void PunchHeldItemByName( string childName, Vector3 pos, Angles rot, float duration )
	{
		if ( string.IsNullOrEmpty( childName ) || IsProxy ) return;
		var held = GameObject.Children.FirstOrDefault( c => c != null && c.Name == "heldItems" );
		if ( held == null ) return;
		foreach ( var ch in held.Children )
		{
			if ( ch == null || !ch.Enabled ) continue;
			if ( ch.Name != childName ) continue;
			var off = ch.Components.Get<FirstPersonHeldItemOffset>( FindMode.EverythingInSelfAndDescendants );
			off?.Punch( pos, rot, duration );
			return;
		}
	}

	/// <summary>
	/// Дёргает FP-оффсет первого ВКЛЮЧЁННОГО предмета под heldItems, чьё имя
	/// начинается с указанного префикса (например "knife" — подходит и
	/// knife_default, и knife_katana, и knife_golden).
	/// </summary>
	public void PunchActiveHeldItemByPrefix( string prefix, Vector3 pos, Angles rot, float duration )
	{
		if ( string.IsNullOrEmpty( prefix ) || IsProxy ) return;
		// Ищем во всех контейнерах под player'ом, чьё имя начинается с "heldItems"
		// — это и старый общий heldItems, и новый heldItemsKnives (один FP-offset
		// на всю группу ножей).
		foreach ( var held in GameObject.Children )
		{
			if ( held == null || !held.Name.StartsWith( "heldItems", System.StringComparison.OrdinalIgnoreCase ) )
				continue;
			foreach ( var ch in held.Children )
			{
				if ( ch == null || !ch.Enabled ) continue;
				if ( !ch.Name.StartsWith( prefix, System.StringComparison.OrdinalIgnoreCase ) ) continue;
				// Сначала пробуем оффсет на самом предмете; если его там нет
				// (новый knife без своего FirstPersonHeldItemOffset) — берём
				// групповой оффсет с контейнера heldItemsKnives.
				var off = ch.Components.Get<FirstPersonHeldItemOffset>( FindMode.EverythingInSelfAndDescendants )
				          ?? held.Components.Get<FirstPersonHeldItemOffset>();
				if ( off != null )
				{
					off.Punch( pos, rot, duration );
					return;
				}
			}
		}
	}

	/// <summary>
	/// Двухключевой взмах через экран (для ножа). Гун проходит по дуге от
	/// startPos/startRot к endPos/endRot, потом возвращается в нейтраль.
	/// </summary>
	public void SwingActiveHeldItemByPrefix( string prefix, Vector3 startPos, Angles startRot,
		Vector3 endPos, Angles endRot, float duration )
	{
		if ( string.IsNullOrEmpty( prefix ) || IsProxy ) return;
		foreach ( var held in GameObject.Children )
		{
			if ( held == null || !held.Name.StartsWith( "heldItems", System.StringComparison.OrdinalIgnoreCase ) )
				continue;
			foreach ( var ch in held.Children )
			{
				if ( ch == null || !ch.Enabled ) continue;
				if ( !ch.Name.StartsWith( prefix, System.StringComparison.OrdinalIgnoreCase ) ) continue;
				var off = ch.Components.Get<FirstPersonHeldItemOffset>( FindMode.EverythingInSelfAndDescendants )
				          ?? held.Components.Get<FirstPersonHeldItemOffset>();
				if ( off != null )
				{
					off.PunchSwing( startPos, startRot, endPos, endRot, duration );
					return;
				}
			}
		}
	}

	// ── Косметика и кристаллы ───────────────────────────────────────────────

	/// <summary>
	/// Актуальное значение EquippedCosmeticsSerialized с учётом RPC-override'a.
	/// На прокси внутри BroadcastRefreshClothingForProxies может быть установлен
	/// _overrideEquippedSerialized — он отдаётся вместо [Sync] поля, чтобы не
	/// зависеть от тайминга его пробрасывания.
	/// </summary>
	public string GetEffectiveEquippedSerialized()
		=> _overrideEquippedSerialized ?? EquippedCosmeticsSerialized;

	public string GetEquipped( string slot )
	{
		if ( string.IsNullOrEmpty( slot ) ) return null;
		var src = GetEffectiveEquippedSerialized();
		if ( string.IsNullOrEmpty( src ) ) return null;
		foreach ( var pair in src.Split( ';', System.StringSplitOptions.RemoveEmptyEntries ) )
		{
			var idx = pair.IndexOf( ':' );
			if ( idx <= 0 ) continue;
			if ( pair.Substring( 0, idx ) == slot )
				return pair.Substring( idx + 1 );
		}
		return null;
	}

	private void SetEquippedLocal( string slot, string id )
	{
		if ( string.IsNullOrEmpty( slot ) ) return;
		var dict = new System.Collections.Generic.Dictionary<string, string>();
		foreach ( var pair in (EquippedCosmeticsSerialized ?? "").Split( ';', System.StringSplitOptions.RemoveEmptyEntries ) )
		{
			var idx = pair.IndexOf( ':' );
			if ( idx <= 0 ) continue;
			dict[pair.Substring( 0, idx )] = pair.Substring( idx + 1 );
		}
		dict[slot] = id ?? "";
		var sb = new System.Text.StringBuilder();
		foreach ( var kv in dict )
		{
			if ( sb.Length > 0 ) sb.Append( ';' );
			sb.Append( kv.Key ).Append( ':' ).Append( kv.Value );
		}
		EquippedCosmeticsSerialized = sb.ToString();
	}

	public bool IsOwned( string slot, string id )
	{
		if ( string.IsNullOrEmpty( slot ) || string.IsNullOrEmpty( id ) ) return false;
		// Default-варианты слотов считаются всегда «обладаемыми» — игроку не нужно их покупать.
		if ( id == "knife_default" || id == "gun_default" || id == "back_none" || id == "hat_none" || id == "nick_default" ) return true;
		// Базовые эмоции (CosmeticCatalog.DefaultEmoteWheel) — у всех всегда есть.
		if ( IsDefaultEmote( id ) ) return true;
		return _owned.TryGetValue( slot, out var counts ) && counts.TryGetValue( id, out var n ) && n > 0;
	}

	/// <summary>Сколько единиц этого предмета у игрока (1 = один, 2+ = дубликаты).</summary>
	public int OwnedCountOf( string slot, string id )
	{
		if ( string.IsNullOrEmpty( slot ) || string.IsNullOrEmpty( id ) ) return 0;
		if ( id == "knife_default" || id == "gun_default" || id == "back_none" || id == "hat_none" || id == "nick_default" ) return 1;
		if ( IsDefaultEmote( id ) ) return 1;
		if ( !_owned.TryGetValue( slot, out var counts ) ) return 0;
		return counts.TryGetValue( id, out var n ) ? n : 0;
	}

	private static bool IsDefaultEmote( string id )
	{
		var defaults = CosmeticCatalog.DefaultEmoteWheel;
		for ( int i = 0; i < defaults.Length; i++ )
			if ( defaults[i] == id ) return true;
		return false;
	}

	// ── Слоты колеса эмоций ───────────────────────────────────────────────
	// Хранение: EmoteWheelSerialized = "id;id;id;id;id;id" (ровно 6).
	// Если строка пустая или короче — отдаём дефолт из CosmeticCatalog.

	public const int EmoteWheelSize = 6;

	public string GetEmoteSlot( int idx )
	{
		if ( idx < 0 || idx >= EmoteWheelSize ) return "";
		var raw = EmoteWheelSerialized;
		if ( !string.IsNullOrEmpty( raw ) )
		{
			var parts = raw.Split( ';' );
			if ( parts.Length == EmoteWheelSize && !string.IsNullOrEmpty( parts[idx] ) )
				return parts[idx];
		}
		var defaults = CosmeticCatalog.DefaultEmoteWheel;
		return idx < defaults.Length ? defaults[idx] : "";
	}

	private void SetEmoteSlotLocal( int idx, string emoteId )
	{
		if ( idx < 0 || idx >= EmoteWheelSize ) return;

		// Раскладываем текущий массив (с подменой дефолтов на отсутствующих позициях).
		var arr = new string[EmoteWheelSize];
		for ( int i = 0; i < EmoteWheelSize; i++ )
			arr[i] = GetEmoteSlot( i );

		arr[idx] = string.IsNullOrEmpty( emoteId ) ? CosmeticCatalog.DefaultEmoteWheel[idx] : emoteId;

		var sb = new System.Text.StringBuilder();
		for ( int i = 0; i < EmoteWheelSize; i++ )
		{
			if ( i > 0 ) sb.Append( ';' );
			sb.Append( arr[i] );
		}
		EmoteWheelSerialized = sb.ToString();
	}

	[Rpc.Broadcast]
	public void RequestSetEmoteSlot( int slotIdx, string emoteId )
	{
		// Авторитет — владелец.
		if ( IsProxy ) return;

		// Anti-cheat: только сам владелец может менять свои слоты.
		var caller = Rpc.Caller;
		if ( caller != null && Network.Owner != null && caller != Network.Owner )
		{
			Log.Warning( $"[ANTICHEAT] RequestSetEmoteSlot rejected: foreign caller." );
			return;
		}

		if ( slotIdx < 0 || slotIdx >= EmoteWheelSize ) return;
		if ( string.IsNullOrEmpty( emoteId ) ) return;

		// Игрок должен владеть эмоцией (дефолтные считаются «owned» автоматически).
		if ( !IsOwned( "emote", emoteId ) )
		{
			Log.Warning( $"[Emote] {emoteId} not owned — can't put in wheel slot {slotIdx}" );
			return;
		}

		SetEmoteSlotLocal( slotIdx, emoteId );

		// Сохранение в облако.
		StatsPersistence.StoreFrom( this, GetSteamIdString() );
	}

	/// <summary>Bridge from StatsPersistence.ApplyTo — загружает сохранённое колесо.</summary>
	public void LoadEmoteWheelFrom( string[] wheel )
	{
		if ( wheel == null || wheel.Length < EmoteWheelSize )
		{
			EmoteWheelSerialized = ""; // отдаст дефолты через GetEmoteSlot
			return;
		}
		var sb = new System.Text.StringBuilder();
		for ( int i = 0; i < EmoteWheelSize; i++ )
		{
			if ( i > 0 ) sb.Append( ';' );
			sb.Append( wheel[i] ?? "" );
		}
		EmoteWheelSerialized = sb.ToString();
	}

	/// <summary>Bridge to StatsPersistence.StoreFrom — отдаёт массив для сериализации.</summary>
	public string[] SaveEmoteWheelInto()
	{
		var arr = new string[EmoteWheelSize];
		for ( int i = 0; i < EmoteWheelSize; i++ )
			arr[i] = GetEmoteSlot( i );
		return arr;
	}

	private void AddOwnedLocal( string slot, string id, int amount = 1 )
	{
		if ( string.IsNullOrEmpty( slot ) || string.IsNullOrEmpty( id ) || amount <= 0 ) return;
		if ( !_owned.TryGetValue( slot, out var counts ) )
		{
			counts = new System.Collections.Generic.Dictionary<string, int>();
			_owned[slot] = counts;
		}
		counts.TryGetValue( id, out var cur );
		counts[id] = cur + amount;
	}

	/// <summary>
	/// Снимает с инвентаря игрока N единиц указанного предмета. Возвращает
	/// true если успешно (было ≥ amount единиц), false если не хватило.
	/// Дефолтные/«пустые» состояния слота снять нельзя — они виртуальные.
	/// Если у игрока этот предмет был экипирован и удалили последнюю копию —
	/// откатываем экипировку на дефолт слота.
	/// </summary>
	private bool RemoveOwnedLocal( string slot, string id, int amount = 1 )
	{
		if ( string.IsNullOrEmpty( slot ) || string.IsNullOrEmpty( id ) || amount <= 0 ) return false;
		// Виртуальные дефолты: снять нельзя.
		if ( id == "knife_default" || id == "gun_default" || id == "back_none" || id == "hat_none" || id == "nick_default" ) return false;
		// Базовые эмоции — тоже виртуальные дефолты, защищаем от трейд-эксплойтов.
		if ( IsDefaultEmote( id ) ) return false;
		if ( !_owned.TryGetValue( slot, out var counts ) ) return false;
		if ( !counts.TryGetValue( id, out var cur ) || cur < amount ) return false;
		int next = cur - amount;
		if ( next <= 0 ) counts.Remove( id );
		else counts[id] = next;

		if ( !counts.TryGetValue( id, out var remaining ) || remaining <= 0 )
		{
			if ( GetEquipped( slot ) == id )
			{
				var parsed = CosmeticCatalog.ParseSlot( slot );
				var def = parsed.HasValue ? CosmeticCatalog.DefaultIdForSlot( parsed.Value ) : null;
				SetEquippedLocal( slot, def ?? "" );
			}
		}
		return true;
	}

	public System.Collections.Generic.IEnumerable<string> OwnedInSlot( string slot )
	{
		if ( string.IsNullOrEmpty( slot ) ) yield break;
		if ( !_owned.TryGetValue( slot, out var counts ) ) yield break;
		foreach ( var kv in counts )
			if ( kv.Value > 0 ) yield return kv.Key;
	}

	/// <summary>Bridge from StatsPersistence.ApplyTo — заливает сохранённые данные в инстанс.</summary>
	public void LoadCosmeticsFrom(
		System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> owned,
		System.Collections.Generic.Dictionary<string, string> equipped )
	{
		_owned.Clear();
		if ( owned != null )
		{
			foreach ( var kv in owned )
			{
				if ( kv.Value == null ) continue;
				// Список может содержать повторяющиеся id — каждое вхождение = +1 к счётчику.
				var counts = new System.Collections.Generic.Dictionary<string, int>();
				foreach ( var id in kv.Value )
				{
					counts.TryGetValue( id, out var n );
					counts[id] = n + 1;
				}
				_owned[kv.Key] = counts;
			}
		}

		// Строим словарь экипировки, вставляя дефолты для слотов без выбора (#3).
		// Новые игроки получат knife_default и nick_default как экипированные с первого захода.
		var equippedMap = new System.Collections.Generic.Dictionary<string, string>();
		if ( equipped != null )
			foreach ( var kv in equipped )
				equippedMap[kv.Key] = kv.Value ?? "";

		if ( !equippedMap.ContainsKey( "knife" ) )    equippedMap["knife"]    = "knife_default";
		if ( !equippedMap.ContainsKey( "gun" ) )      equippedMap["gun"]      = "gun_default";
		if ( !equippedMap.ContainsKey( "nickname" ) ) equippedMap["nickname"] = "nick_default";

		var sb = new System.Text.StringBuilder();
		foreach ( var kv in equippedMap )
		{
			if ( sb.Length > 0 ) sb.Append( ';' );
			sb.Append( kv.Key ).Append( ':' ).Append( kv.Value );
		}
		EquippedCosmeticsSerialized = sb.ToString();
	}

	/// <summary>Bridge to StatsPersistence.StoreFrom — выгружает текущее состояние в словари.</summary>
	public void SaveCosmeticsInto(
		System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> owned,
		System.Collections.Generic.Dictionary<string, string> equipped )
	{
		owned.Clear();
		foreach ( var kv in _owned )
		{
			var list = new System.Collections.Generic.List<string>();
			// Повторяем id столько раз, сколько единиц в инвентаре.
			foreach ( var pair in kv.Value )
				for ( int i = 0; i < pair.Value; i++ )
					list.Add( pair.Key );
			owned[kv.Key] = list;
		}

		equipped.Clear();
		foreach ( var pair in (EquippedCosmeticsSerialized ?? "").Split( ';', System.StringSplitOptions.RemoveEmptyEntries ) )
		{
			var idx = pair.IndexOf( ':' );
			if ( idx <= 0 ) continue;
			equipped[pair.Substring( 0, idx )] = pair.Substring( idx + 1 );
		}
	}

	// Максимум кристаллов из раундов за один день (UTC). Блокирует фарм с сокс-аккаунтами.
	[Property, Group("Crystal Rewards")] public int DailyCrystalCap { get; set; } = 1500;

	[Rpc.Broadcast]
	public void GrantCrystals( int amount, bool bypassDailyCap = false )
	{
		// Хост вызывает легитимно из GameRoom.AwardStats (награды раунда),
		// админ — из AdminPanelUI / DebugCommands. Оба варианта разрешены.
		// bypassDailyCap=true пропускает дневной лимит — используется ТОЛЬКО
		// админ-инструментами, чтобы выдача из админ-панели не упиралась в
		// антифарм-cap, который рассчитан на обычных игроков.
		var caller = Rpc.Caller;
		bool allowed = caller == null || caller.IsHost || AdminList.IsAdmin( caller );
		if ( !allowed )
		{
			Log.Warning( $"[ANTICHEAT] GrantCrystals rejected: caller '{caller.DisplayName}' is not host/admin." );
			return;
		}
		if ( IsProxy ) return;
		if ( amount <= 0 ) return;

		// Дневной лимит кристаллов из раундов. Сбрасывается в UTC-полночь.
		// Админский grant (bypassDailyCap=true) лимит игнорирует, но всё
		// равно НЕ накручивает DailyCrystalsEarned — иначе обычные награды
		// после админ-раздачи будут резаться лимитом.
		if ( !bypassDailyCap )
		{
			var sid = GetSteamIdString();
			if ( !string.IsNullOrEmpty( sid ) )
			{
				var entry    = StatsPersistence.GetFor( sid );
				string today = System.DateTime.UtcNow.ToString( "yyyy-MM-dd" );
				if ( entry.DailyCrystalsDate != today )
				{
					entry.DailyCrystalsDate   = today;
					entry.DailyCrystalsEarned = 0;
				}
				int cap     = DailyCrystalCap;
				int allowed2 = System.Math.Max( 0, cap - entry.DailyCrystalsEarned );
				amount = System.Math.Min( amount, allowed2 );
				if ( amount <= 0 ) return;
				entry.DailyCrystalsEarned += amount;
			}
		}

		Crystals += amount;
		// Variant 3: owner сам сохраняет свой локальный персист.
		StatsPersistence.StoreFrom( this, GetSteamIdString() );
	}

	[Rpc.Broadcast]
	public void RequestBuyCosmetic( string itemId )
	{
		// Покупка — авторитет владельца (он держит кошелёк и Owned).
		if ( IsProxy ) return;
		// Anti-cheat: только сам владелец может вызвать у себя покупку.
		var caller = Rpc.Caller;
		if ( caller != null && Network.Owner != null && caller != Network.Owner )
		{
			Log.Warning( $"[ANTICHEAT] RequestBuyCosmetic rejected: foreign caller." );
			return;
		}
		var item = CosmeticCatalog.Get( itemId );
		if ( item == null ) { Log.Warning( $"[Cosmetics] unknown id: {itemId}" ); return; }

		var slot = CosmeticCatalog.SlotKey( item.Slot );
		if ( IsOwned( slot, itemId ) ) return;
		if ( Crystals < item.Price )
		{
			Log.Warning( $"[Cosmetics] not enough crystals: have {Crystals}, need {item.Price}" );
			return;
		}

		Crystals -= item.Price;
		AddOwnedLocal( slot, itemId );

		// Variant 3: owner сам сохраняет свой локальный персист.
		StatsPersistence.StoreFrom( this, GetSteamIdString() );
	}

	[Rpc.Broadcast]
	public void RequestEquipCosmetic( string slot, string itemId )
	{
		if ( IsProxy ) return;
		var caller = Rpc.Caller;
		if ( caller != null && Network.Owner != null && caller != Network.Owner )
		{
			Log.Warning( $"[ANTICHEAT] RequestEquipCosmetic rejected: foreign caller." );
			return;
		}
		if ( string.IsNullOrEmpty( slot ) || string.IsNullOrEmpty( itemId ) ) return;
		if ( !IsOwned( slot, itemId ) ) return;

		SetEquippedLocal( slot, itemId );

		Log.Info( $"[COSM] RequestEquip player={DisplayName} slot='{slot}' itemId='{itemId}' newSync='{EquippedCosmeticsSerialized}'" );

		// Если у предмета метод Clothing — нужно перенакатить аватар через ClothingContainer.
		var item = CosmeticCatalog.Get( itemId );
		if ( item != null && item.Method == CosmeticCatalog.ApplyMethod.Clothing )
			ApplyClothing();
		// Если игрок снимает clothing-косметику в этом слоте — тоже перенакатить.
		else if ( itemId == CosmeticCatalog.DefaultIdForSlot( CosmeticCatalog.ParseSlot( slot ) ?? CosmeticCatalog.Slot.Hat ) )
			ApplyClothing();
		// Accessory тоже нужно перенакатить.
		else if ( item != null && item.Method == CosmeticCatalog.ApplyMethod.Accessory )
			ApplyClothing();

		// Передаём новое значение явно в RPC параметре — не полагаемся на
		// тайминг распространения [Sync] EquippedCosmeticsSerialized.
		Log.Info( $"[COSM] sending Broadcast player={DisplayName} newSerialized='{EquippedCosmeticsSerialized}'" );
		BroadcastRefreshClothingForProxies( EquippedCosmeticsSerialized ?? "" );

		StatsPersistence.StoreFrom( this, GetSteamIdString() );
	}

	/// <summary>
	/// Owner → всем прокси: «сбрось кэш и перенакати ApplyClothing локально».
	/// Передаём новое значение EquippedCosmeticsSerialized явно в параметре,
	/// чтобы не зависеть от тайминга [Sync] (он может ещё не пробросить значение
	/// к моменту прихода RPC).
	/// </summary>
	[Rpc.Broadcast]
	private void BroadcastRefreshClothingForProxies( string newEquippedSerialized )
	{
		Log.Info( $"[COSM] RPC arrived player={DisplayName} IsProxy={IsProxy} newSerialized='{newEquippedSerialized}' sync='{EquippedCosmeticsSerialized}'" );
		// Owner уже сделал свой ApplyClothing локально — ему не нужно делать ещё раз.
		if ( !IsProxy ) return;

		// Сохраняем оверрайд который ApplyClothing/GetEquipped будут читать
		// вместо [Sync] поля. Это страхует от случая когда RPC пришёл раньше
		// чем sync-значение успело прорасти на прокси.
		_overrideEquippedSerialized = newEquippedSerialized;
		try
		{
			ApplyClothing();
		}
		finally
		{
			_overrideEquippedSerialized = null;
		}
		Log.Info( $"[COSM] RPC done player={DisplayName}" );
	}

	// Временный оверрайд EquippedCosmeticsSerialized — устанавливается RPC'ой
	// BroadcastRefreshClothingForProxies на прокси чтобы ApplyClothing/GetEquipped
	// читали свежее значение даже если [Sync] ещё не пришёл.
	private string _overrideEquippedSerialized = null;

	// ── Эмоции ──────────────────────────────────────────────────────────────

	private const float EmoteDurationSeconds = 5f;

	[Rpc.Broadcast]
	public void PlayEmote( string emoteId )
	{
		// Авторитет — владелец: только он пишет Sync-поля.
		if ( IsProxy ) return;

		// Anti-cheat: вызывать может только сам владелец.
		var caller = Rpc.Caller;
		if ( caller != null && Network.Owner != null && caller != Network.Owner )
		{
			Log.Warning( $"[ANTICHEAT] PlayEmote rejected: foreign caller." );
			return;
		}

		if ( string.IsNullOrEmpty( emoteId ) ) return;

		// Проверка владения — базовые эмоции у всех есть, остальные должны
		// быть куплены/выпасть из кейса.
		if ( !IsOwned( "emote", emoteId ) )
		{
			Log.Warning( $"[Emote] {emoteId} not owned" );
			return;
		}

		// Сервер-сайд кулдаун (поверх клиентского в EmoteWheelUI).
		// Duration == Cooldown, поэтому пока прошлая эмоция не отыграла — игнорим.
		if ( Time.Now < ActiveEmoteEndAt ) return;

		ActiveEmoteId    = emoteId;
		ActiveEmoteEndAt = Time.Now + EmoteDurationSeconds;
	}

	// ── Кейсы ───────────────────────────────────────────────────────────────

	public int GetCaseCount( string caseId )
	{
		if ( string.IsNullOrEmpty( caseId ) ) return 0;
		return _ownedCases.TryGetValue( caseId, out var n ) ? n : 0;
	}

	/// <summary>Список (id, count) всех кейсов в инвентаре игрока.</summary>
	public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, int>> AllOwnedCases()
	{
		foreach ( var kv in _ownedCases )
			if ( kv.Value > 0 ) yield return kv;
	}

	private void AddCaseLocal( string caseId, int count = 1 )
	{
		if ( string.IsNullOrEmpty( caseId ) || count <= 0 ) return;
		_ownedCases.TryGetValue( caseId, out var cur );
		_ownedCases[caseId] = cur + count;
	}

	/// <summary>Bridge from StatsPersistence.ApplyTo.</summary>
	public void LoadCasesFrom( System.Collections.Generic.Dictionary<string, int> source )
	{
		_ownedCases.Clear();
		if ( source == null ) return;
		foreach ( var kv in source )
			if ( kv.Value > 0 ) _ownedCases[kv.Key] = kv.Value;
	}

	/// <summary>Bridge to StatsPersistence.StoreFrom.</summary>
	public void SaveCasesInto( System.Collections.Generic.Dictionary<string, int> target )
	{
		target.Clear();
		foreach ( var kv in _ownedCases )
			if ( kv.Value > 0 ) target[kv.Key] = kv.Value;
	}

	// ── Промокоды (персист) ────────────────────────────────────────────────

	public bool HasRedeemedPromo( string code )
	{
		return _redeemedPromos.Contains( PromoCodeManager.NormalizeKey( code ) );
	}

	public void LoadPromosFrom( System.Collections.Generic.List<string> source )
	{
		_redeemedPromos.Clear();
		if ( source == null ) return;
		foreach ( var c in source )
			if ( !string.IsNullOrEmpty( c ) ) _redeemedPromos.Add( PromoCodeManager.NormalizeKey( c ) );
	}

	public void SavePromosInto( System.Collections.Generic.List<string> target )
	{
		target.Clear();
		foreach ( var c in _redeemedPromos ) target.Add( c );
	}

	private void SetPromoFeedback( string key, bool ok, string arg = "" )
	{
		LastPromoFeedbackKey = key ?? "";
		LastPromoFeedbackArg = arg ?? "";
		LastPromoSuccess = ok;
		PromoFeedbackCounter++;
	}

	// ── Ежедневки (персист + claim) ─────────────────────────────────────────

	public void LoadDailyFrom( string lastDate, int streak, string lastGameDate = "" )
	{
		LastDailyClaimDate = lastDate ?? "";
		DailyClaimStreak = System.Math.Clamp( streak, 0, 7 );
		LastGamePlayedDate = lastGameDate ?? "";
	}

	private static string TodayKey() => System.DateTime.UtcNow.Date.ToString( "yyyy-MM-dd" );

	/// <summary>True если игрок сегодня уже сыграл хотя бы одну партию.</summary>
	public bool HasPlayedGameToday()
	{
		return LastGamePlayedDate == TodayKey();
	}

	/// <summary>True если игрок ещё не забрал сегодняшнюю награду И уже сыграл сегодня партию.</summary>
	public bool CanClaimDailyToday()
	{
		return LastDailyClaimDate != TodayKey() && HasPlayedGameToday();
	}

	/// <summary>
	/// Хост-only: помечает игрока как «сыграл сегодня партию». Вызывается из GameRoom
	/// в конце раунда для каждого участника (вне зависимости от победы/поражения).
	/// </summary>
	[Rpc.Broadcast]
	public void MarkPlayedToday()
	{
		var caller = Rpc.Caller;
		if ( caller != null && !caller.IsHost )
		{
			Log.Warning( $"[ANTICHEAT] MarkPlayedToday rejected: non-host caller." );
			return;
		}
		if ( IsProxy ) return;
		var today = TodayKey();
		if ( LastGamePlayedDate == today ) return;
		LastGamePlayedDate = today;
		StatsPersistence.StoreFrom( this, GetSteamIdString() );
	}

	/// <summary>Какой день (1..7) забирается следующим (для подсветки в UI).</summary>
	public int NextDailyDay()
	{
		var today = System.DateTime.UtcNow.Date;
		if ( DailyClaimStreak <= 0 ) return 1;
		if ( !System.DateTime.TryParse( LastDailyClaimDate, out var stored ) ) return 1;
		var diff = (int)(today - stored.Date).TotalDays;
		if ( diff == 0 ) return DailyClaimStreak; // уже забрал сегодня — «текущий» день
		if ( diff == 1 )
		{
			int next = DailyClaimStreak + 1;
			return next > 7 ? 1 : next;
		}
		return 1; // стрик прерван
	}

	private void SetDailyFeedback( string key, bool ok, string arg = "" )
	{
		LastDailyFeedbackKey = key ?? "";
		LastDailyFeedbackArg = arg ?? "";
		LastDailySuccess = ok;
		DailyFeedbackCounter++;
	}

	/// <summary>
	/// Игрок жмёт «Получить» в DailyRewardsUI. Owner-side проверка стрика
	/// и применение награды с записью в персист.
	/// </summary>
	[Rpc.Broadcast]
	public void RequestClaimDaily()
	{
		if ( IsProxy ) return;
		var caller = Rpc.Caller;
		if ( caller != null && Network.Owner != null && caller != Network.Owner )
		{
			Log.Warning( $"[ANTICHEAT] RequestClaimDaily rejected: foreign caller." );
			return;
		}

		var mgr = DailyRewardsManager.Instance;
		if ( mgr == null || mgr.RewardCount == 0 )
		{
			SetDailyFeedback( "daily.bad.unavailable", false );
			return;
		}

		var today = System.DateTime.UtcNow.Date;
		bool hasStored = System.DateTime.TryParse( LastDailyClaimDate, out var stored );
		if ( hasStored && stored.Date == today )
		{
			SetDailyFeedback( "daily.bad.already", false );
			return;
		}

		if ( !HasPlayedGameToday() )
		{
			SetDailyFeedback( "daily.bad.no_game", false );
			return;
		}

		// Какой день стрика забираем сейчас.
		int day;
		if ( !hasStored || DailyClaimStreak <= 0 )
		{
			day = 1;
		}
		else
		{
			int diff = (int)(today - stored.Date).TotalDays;
			if ( diff == 1 )
			{
				day = DailyClaimStreak + 1;
				if ( day > 7 ) day = 1;
			}
			else
			{
				day = 1; // пропуск — стрик заново
			}
		}

		var reward = mgr.GetReward( day - 1 );
		if ( reward == null )
		{
			SetDailyFeedback( "daily.bad.unavailable", false );
			return;
		}

		string label = ApplyDailyReward( reward );

		DailyClaimStreak = day;
		LastDailyClaimDate = today.ToString( "yyyy-MM-dd" );
		StatsPersistence.StoreFrom( this, GetSteamIdString() );
		SetDailyFeedback( "daily.success", true, label );
		Log.Info( $"[Daily] {DisplayName} claimed day {day} ({reward.Kind}: {label})" );

		if ( DailyClaimStreak >= 7 )
			try { Sandbox.Services.Achievements.Unlock( "mm_daily_streak_" ); } catch { }
	}

	private string ApplyDailyReward( DailyRewardsManager.DailyReward r )
	{
		switch ( r.Kind )
		{
			case DailyRewardsManager.RewardKind.Crystals:
				if ( r.CrystalsAmount > 0 ) Crystals += r.CrystalsAmount;
				return $"+{r.CrystalsAmount} 💎";

			case DailyRewardsManager.RewardKind.Case:
				if ( !string.IsNullOrEmpty( r.CaseId ) && r.CaseCount > 0 )
					AddCaseLocal( r.CaseId, r.CaseCount );
				return $"+{r.CaseCount}× {r.CaseId}";

			case DailyRewardsManager.RewardKind.Cosmetic:
				if ( !string.IsNullOrEmpty( r.CosmeticSlot ) && !string.IsNullOrEmpty( r.CosmeticId ) )
					AddOwnedLocal( r.CosmeticSlot, r.CosmeticId, 1 );
				var item = CosmeticCatalog.Get( r.CosmeticId );
				return item != null ? L.T( item.NameKey ) : r.CosmeticId;
		}
		return "";
	}

	/// <summary>
	/// Игрок ввёл промокод в UI — owner сам проверяет код в локальном
	/// PromoCodeManager (общий список зашит в сцену) и применяет награду.
	/// </summary>
	[Rpc.Broadcast]
	public void RequestRedeemPromo( string code )
	{
		if ( IsProxy ) return;
		var caller = Rpc.Caller;
		if ( caller != null && Network.Owner != null && caller != Network.Owner )
		{
			Log.Warning( $"[ANTICHEAT] RequestRedeemPromo rejected: foreign caller." );
			return;
		}
		if ( string.IsNullOrWhiteSpace( code ) )
		{
			SetPromoFeedback( "promo.bad.empty", false );
			return;
		}

		var mgr = PromoCodeManager.Instance;
		if ( mgr == null )
		{
			Log.Warning( "[Promo] PromoCodeManager not on scene." );
			SetPromoFeedback( "promo.bad.unknown", false );
			return;
		}

		var def = mgr.FindCode( code );
		if ( def == null )
		{
			SetPromoFeedback( "promo.bad.unknown", false );
			return;
		}

		var key = PromoCodeManager.NormalizeKey( def.Code );
		if ( def.OneTimePerPlayer && _redeemedPromos.Contains( key ) )
		{
			SetPromoFeedback( "promo.bad.used", false );
			return;
		}

		string rewardLabel = "";
		switch ( def.Kind )
		{
			case PromoCodeManager.RewardKind.Crystals:
				if ( def.CrystalsAmount > 0 )
				{
					Crystals += def.CrystalsAmount;
					rewardLabel = $"+{def.CrystalsAmount} 💎";
				}
				else
				{
					SetPromoFeedback( "promo.bad.unknown", false );
					return;
				}
				break;

			case PromoCodeManager.RewardKind.Case:
				if ( string.IsNullOrEmpty( def.CaseId ) || def.CaseCount <= 0 )
				{
					SetPromoFeedback( "promo.bad.unknown", false );
					return;
				}
				AddCaseLocal( def.CaseId, def.CaseCount );
				rewardLabel = $"+{def.CaseCount}× {def.CaseId}";
				break;

			case PromoCodeManager.RewardKind.Cosmetic:
				if ( string.IsNullOrEmpty( def.CosmeticSlot ) || string.IsNullOrEmpty( def.CosmeticId ) )
				{
					SetPromoFeedback( "promo.bad.unknown", false );
					return;
				}
				AddOwnedLocal( def.CosmeticSlot, def.CosmeticId, 1 );
				var item = CosmeticCatalog.Get( def.CosmeticId );
				rewardLabel = item != null ? L.T( item.NameKey ) : def.CosmeticId;
				break;
		}

		_redeemedPromos.Add( key );
		StatsPersistence.StoreFrom( this, GetSteamIdString() );
		SetPromoFeedback( "promo.success", true, rewardLabel );
		Log.Info( $"[Promo] '{def.Code}' redeemed by {DisplayName} ({def.Kind}: {rewardLabel})" );
		try { Sandbox.Services.Achievements.Unlock( "mm_promo_redeeme" ); } catch { }
	}

	/// <summary>
	/// Игрок просит купить кейс у NPC. Авторитет — владелец (он держит кошелёк).
	/// vendorId — Guid GameObject'а NPC, нужен чтобы взять прайс-лист с правильного торговца
	/// (один и тот же id кейса теоретически может стоить по-разному у разных вендоров).
	/// </summary>
	[Rpc.Broadcast]
	public void RequestBuyCase( System.Guid vendorId, string caseId )
	{
		if ( IsProxy ) return;
		var caller = Rpc.Caller;
		if ( caller != null && Network.Owner != null && caller != Network.Owner )
		{
			Log.Warning( $"[ANTICHEAT] RequestBuyCase rejected: foreign caller." );
			return;
		}
		if ( string.IsNullOrEmpty( caseId ) ) return;

		var vendor = Scene.GetAllComponents<CosmeticVendorNPC>()
			.FirstOrDefault( v => v.GameObject.Id == vendorId );
		if ( vendor == null ) { Log.Warning( $"[Cases] vendor {vendorId} not found" ); return; }

		var def = vendor.Cases?.FirstOrDefault( c => c.Id == caseId );
		if ( def == null ) { Log.Warning( $"[Cases] vendor doesn't sell case '{caseId}'" ); return; }

		if ( Crystals < def.Price )
		{
			Log.Warning( $"[Cases] not enough crystals: have {Crystals}, need {def.Price}" );
			return;
		}

		Crystals -= def.Price;
		AddCaseLocal( caseId, 1 );

		// Variant 3: owner сам сохраняет свой локальный персист.
		StatsPersistence.StoreFrom( this, GetSteamIdString() );
	}

	// ── Админ-выдача (только хост) ─────────────────────────────────────────

	/// <summary>
	/// Хост выдаёт кейс любому игроку. Broadcast — но обрабатывает только owner.
	/// </summary>
	[Rpc.Broadcast]
	public void AdminGrantCase( string caseId, int count = 1 )
	{
		// Раньше проверялся IsHost — но «хост ≠ админ». Теперь смотрим в AdminList:
		// права выдаются только тем, чей SteamId явно прописан в whitelist.
		var caller = Rpc.Caller;
		if ( caller != null && !AdminList.IsAdmin( caller ) )
		{
			Log.Warning( $"[ANTICHEAT] AdminGrantCase rejected: caller '{caller.DisplayName}' is not admin." );
			return;
		}
		if ( IsProxy ) return;
		if ( string.IsNullOrEmpty( caseId ) || count <= 0 ) return;
		AddCaseLocal( caseId, count );
		// Variant 3: owner сам сохраняет свой локальный персист.
		StatsPersistence.StoreFrom( this, GetSteamIdString() );
		Log.Info( $"[Admin] +{count}× case '{caseId}' → {DisplayName}" );
	}

	/// <summary>
	/// Хост выдаёт косметику любому игроку. Broadcast — но обрабатывает только owner.
	/// </summary>
	[Rpc.Broadcast]
	public void AdminGrantCosmetic( string slot, string itemId, int count = 1 )
	{
		var caller = Rpc.Caller;
		if ( caller != null && !AdminList.IsAdmin( caller ) )
		{
			Log.Warning( $"[ANTICHEAT] AdminGrantCosmetic rejected: caller '{caller.DisplayName}' is not admin." );
			return;
		}
		if ( IsProxy ) return;
		if ( string.IsNullOrEmpty( slot ) || string.IsNullOrEmpty( itemId ) || count <= 0 ) return;
		AddOwnedLocal( slot, itemId, count );
		// Variant 3: owner сам сохраняет свой локальный персист.
		StatsPersistence.StoreFrom( this, GetSteamIdString() );
		Log.Info( $"[Admin] +{count}× cosmetic '{slot}/{itemId}' → {DisplayName}" );
	}

	/// <summary>
	/// Игрок открывает кейс. Авторитет — владелец (он же держит инвентарь и знает баланс кейсов).
	/// Логика: проверяем что кейс есть, выбираем дроп с весами, минусуем 1 кейс, добавляем косметику в Owned,
	/// выставляем LastRolledItemId/LastRolledCaseId/LastRollCounter — UI на стороне игрока подхватит и проиграет анимацию.
	/// </summary>
	[Rpc.Broadcast]
	public void RequestOpenCase( System.Guid vendorId, string caseId )
	{
		if ( IsProxy ) return;
		var caller = Rpc.Caller;
		if ( caller != null && Network.Owner != null && caller != Network.Owner )
		{
			Log.Warning( $"[ANTICHEAT] RequestOpenCase rejected: foreign caller." );
			return;
		}
		if ( string.IsNullOrEmpty( caseId ) ) return;
		if ( GetCaseCount( caseId ) <= 0 ) { Log.Warning( $"[Cases] open: no '{caseId}' in inventory" ); return; }

		// Стартовый кейс определён в коде — не у вендоров.
		CaseDefinition def = StarterCase.Get( caseId );
		if ( def == null )
		{
			// Берём прайс/дроп-пул у первого NPC, который знает этот кейс. Vendor-параметр
			// оставляем для совместимости с RequestBuyCase, но если конкретный vendor не нашёлся —
			// попробуем любой.
			var byVendor = Scene.GetAllComponents<CosmeticVendorNPC>()
				.FirstOrDefault( v => v.GameObject.Id == vendorId );
			if ( byVendor != null && byVendor.Cases != null )
				def = byVendor.Cases.FirstOrDefault( c => c.Id == caseId );
			if ( def == null )
			{
				foreach ( var v in Scene.GetAllComponents<CosmeticVendorNPC>() )
				{
					if ( v.Cases == null ) continue;
					def = v.Cases.FirstOrDefault( c => c.Id == caseId );
					if ( def != null ) break;
				}
			}
		}
		if ( def == null || def.Drops == null || def.Drops.Count == 0 )
		{
			Log.Warning( $"[Cases] open: case '{caseId}' has no drops configured" );
			return;
		}

		// Взвешенный рол. Валидный дроп — это либо реальный CosmeticId,
		// либо CrystalsAmount > 0 (произвольный номинал). Вес должен быть > 0.
		static bool IsValidDrop( CaseDropEntry d )
			=> d != null && d.Weight > 0f && (!string.IsNullOrEmpty( d.CosmeticId ) || d.CrystalsAmount > 0);

		float total = 0f;
		foreach ( var d in def.Drops )
			if ( IsValidDrop( d ) ) total += d.Weight;
		if ( total <= 0f )
		{
			Log.Warning( $"[Cases] open: case '{caseId}' total weight is zero" );
			return;
		}

		float roll = (float)(new System.Random().NextDouble()) * total;
		CaseDropEntry rolled = null;
		float acc = 0f;
		foreach ( var d in def.Drops )
		{
			if ( !IsValidDrop( d ) ) continue;
			acc += d.Weight;
			if ( roll <= acc ) { rolled = d; break; }
		}
		rolled ??= def.Drops.Last( IsValidDrop );

		// Декрементим кейс.
		var cur = _ownedCases.TryGetValue( caseId, out var n ) ? n : 0;
		_ownedCases[caseId] = System.Math.Max( 0, cur - 1 );

		string rolledId;

		// Currency-дроп через CrystalsAmount (приоритет — он дизайнерски явный).
		if ( rolled.CrystalsAmount > 0 )
		{
			Crystals += rolled.CrystalsAmount;
			// Для UI отдаём синтетический id, чтобы LastRolledItemId был не пустым.
			rolledId = $"crystals_{rolled.CrystalsAmount}";
		}
		else
		{
			rolledId = rolled.CosmeticId;
			var item = CosmeticCatalog.Get( rolledId );
			if ( item == null )
			{
				Log.Warning( $"[Cases] rolled unknown cosmetic '{rolledId}' in case '{caseId}'" );
				return;
			}

			// Старый путь: currency через ApplyMethod.Currency у item из каталога
			// (crystals_50/100/150/etc). Оставлен для совместимости.
			if ( item.Method == CosmeticCatalog.ApplyMethod.Currency )
			{
				int amount = 0;
				int.TryParse( item.ResourceRef, out amount );
				if ( amount > 0 ) Crystals += amount;
			}
			else
			{
				var slotKey = CosmeticCatalog.SlotKey( item.Slot );
				AddOwnedLocal( slotKey, rolledId );
			}
		}

		// Сообщаем UI результат. Counter инкрементится — UI ловит изменение даже если выпал тот же предмет.
		LastRolledCaseId = caseId;
		LastRolledItemId = rolledId;
		LastRollCounter = LastRollCounter + 1;

		// Variant 3: owner сам сохраняет свой локальный персист.
		StatsPersistence.StoreFrom( this, GetSteamIdString() );

		try { Sandbox.Services.Achievements.Unlock( "mm_case_opened" ); } catch { }
	}

	// ── Трейд между игроками ────────────────────────────────────────────────
	//
	// Архитектура: всё состояние сессии хранится в TradeManager (per-client,
	// runtime). Эти RPC — только сетевая транспортировка событий между
	// инвайтером и приглашаемым. Каждая RPC проверяет, что owner — именно
	// тот игрок, который её вызвал, и что цель (по SteamId) — это мой локальный
	// PlayerStats. Чужие сообщения отбрасываются.
	//
	// Trust-модель: каждый клиент мутирует ТОЛЬКО свой собственный инвентарь.
	// При commit-фазе обе стороны независимо удаляют у себя то, что отдали,
	// и добавляют то, что получили (на основе последнего offer'а партнёра).
	// Это согласуется с тем, как работают промокоды и покупки косметики.

	public string PublicSteamId => GetSteamIdString() ?? "";

	/// <summary>
	/// Возвращает PlayerStats локального игрока (не proxy), или null.
	/// Используется в трейд-RPC чтобы отфильтровать «свои/чужие» клиенты —
	/// сами Rpc.Broadcast выполняются на копии отправителя на каждом клиенте,
	/// поэтому надо проверять «я ли являюсь адресатом» через локальный SteamId.
	/// </summary>
	private static PlayerStats LocalOwnerInScene()
	{
		return Game.ActiveScene?.GetAllComponents<PlayerStats>().FirstOrDefault( p => !p.IsProxy );
	}

	private static bool IsLocalTarget( string toSteamId )
	{
		if ( string.IsNullOrEmpty( toSteamId ) ) return false;
		var local = LocalOwnerInScene();
		return local != null && local.PublicSteamId == toSteamId;
	}

	[Rpc.Broadcast]
	public void TradeInvite( string fromSteamId, string fromName, string toSteamId )
	{
		if ( !IsLocalTarget( toSteamId ) ) return;
		if ( Rpc.Caller != null && Rpc.Caller.SteamId.ToString() != fromSteamId )
		{
			Log.Warning( "[Trade] Invite rejected: caller SteamId mismatch." );
			return;
		}
		TradeManager.OnIncomingInvite( fromSteamId, fromName );
	}

	[Rpc.Broadcast]
	public void TradeInviteResponse( string fromSteamId, string toSteamId, bool accepted, string fromName )
	{
		if ( !IsLocalTarget( toSteamId ) ) return;
		TradeManager.OnInviteResponse( fromSteamId, fromName, accepted );
	}

	[Rpc.Broadcast]
	public void TradeUpdateOffer( string fromSteamId, string toSteamId, string offerSerialized, int crystals, bool ready )
	{
		if ( !IsLocalTarget( toSteamId ) ) return;
		TradeManager.OnPartnerOfferUpdated( fromSteamId, offerSerialized ?? "", crystals, ready );
	}

	[Rpc.Broadcast]
	public void TradeCancel( string fromSteamId, string toSteamId, string reasonKey )
	{
		if ( !IsLocalTarget( toSteamId ) ) return;
		TradeManager.OnPartnerCancelled( fromSteamId, reasonKey ?? "" );
	}

	[Rpc.Broadcast]
	public void TradeChat( string fromSteamId, string toSteamId, string fromName, string text )
	{
		if ( !IsLocalTarget( toSteamId ) ) return;
		TradeManager.OnPartnerChat( fromSteamId, fromName ?? "", text ?? "" );
	}

	/// <summary>
	/// Owner-side: отдаёт перечисленные предметы и кристаллы из своего инвентаря.
	/// Возвращает true если всё прошло, false если чего-то не хватило (тогда
	/// инвентарь не меняется и трейд должен быть отменён).
	/// </summary>
	public bool TradeCommitOutgoing( System.Collections.Generic.List<(string slot, string id)> items, int crystals )
	{
		if ( IsProxy ) return false;
		if ( crystals < 0 ) crystals = 0;
		if ( Crystals < crystals ) return false;

		// Подсчёт количества каждой пары (slot,id) — на стороне может быть
		// несколько одинаковых (несколько одинаковых наклеек/скинов из кейсов).
		var counts = new System.Collections.Generic.Dictionary<string, int>();
		if ( items != null )
		{
			foreach ( var (slot, id) in items )
			{
				if ( string.IsNullOrEmpty( slot ) || string.IsNullOrEmpty( id ) ) continue;
				var key = slot + ":" + id;
				counts.TryGetValue( key, out var c );
				counts[key] = c + 1;
			}
		}

		// Валидация: всё ли в наличии.
		foreach ( var kv in counts )
		{
			var parts = kv.Key.Split( ':', 2 );
			if ( parts.Length != 2 ) return false;
			if ( OwnedCountOf( parts[0], parts[1] ) < kv.Value ) return false;
		}

		// Применение.
		foreach ( var kv in counts )
		{
			var parts = kv.Key.Split( ':', 2 );
			if ( !RemoveOwnedLocal( parts[0], parts[1], kv.Value ) ) return false;
		}
		Crystals -= crystals;
		StatsPersistence.StoreFrom( this, GetSteamIdString() );
		return true;
	}

	/// <summary>
	/// Owner-side: добавляет предметы и кристаллы, полученные от партнёра.
	/// Безусловно — это уже свершившийся факт обмена, проверять нечего.
	/// </summary>
	public void TradeApplyIncoming( System.Collections.Generic.List<(string slot, string id)> items, int crystals )
	{
		if ( IsProxy ) return;
		if ( items != null )
			foreach ( var (slot, id) in items )
				if ( !string.IsNullOrEmpty( slot ) && !string.IsNullOrEmpty( id ) )
					AddOwnedLocal( slot, id, 1 );
		if ( crystals > 0 ) Crystals += crystals;
		StatsPersistence.StoreFrom( this, GetSteamIdString() );
	}
}
