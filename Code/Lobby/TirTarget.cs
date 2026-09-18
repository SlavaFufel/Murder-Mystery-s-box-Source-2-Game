using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Мишень для тира. При попадании (триггер ловит игрока или его hitbox-нож,
/// либо raycast выстрела) — наклоняется вперёд на 90° (как настоящая
/// падающая мишень), через RespawnDelay секунд встаёт обратно.
/// Анимация плавная, на всех клиентах.
///
/// Скоуп через RoomId == TirZone.TirRoomId: вне тира мишень невидима и не реагирует.
/// </summary>
public sealed class TirTarget : Component, Component.ITriggerListener
{
	[Property] public float RespawnDelay { get; set; } = 3f;
	[Property] public string HitSound { get; set; } = "";
	// Скорость лерпа угла — больше = быстрее анимация падения/подъёма.
	[Property] public float AnimationSpeed { get; set; } = 8f;
	// Угол падения. +90 = НАЗАД (от игрока), -90 = вперёд. Можно ставить
	// промежуточные значения если хочется лёгкий наклон.
	[Property] public float FallAngle { get; set; } = 90f;

	// Sync-флаги: все эти поля пишет ХОСТ (не owner объекта, хотя в нашем
	// случае host и есть owner — но SyncFlags.FromHost явно фиксирует это
	// и пережимёт даже если ownership когда-нибудь сменится).
	// Документация: https://sbox.game/dev/doc/systems/networking-multiplayer/sync-properties/
	[Sync( SyncFlags.FromHost )] public bool Hidden { get; set; } = false;
	[Sync( SyncFlags.FromHost )] public System.Guid RoomId { get; set; } = System.Guid.Empty;

	// Идентификатор владельца — GameObject.Id PlayerStats игрока. Guid.Empty
	// означает «шаблон» (мишень из сцены, никому не видна — служит прототипом
	// для клонирования). Не-Empty = клон, видим/стреляем только им.
	[Sync( SyncFlags.FromHost )] public System.Guid OwnerPlayerId { get; set; } = System.Guid.Empty;

	// Ось падения в мире, синкается хостом в момент попадания.
	[Sync( SyncFlags.FromHost )] public Vector3 FallAxisSynced { get; set; } = Vector3.Zero;

	// Время начала текущего перехода (падение или подъём). Используется
	// для детерминированной анимации (Time.Now синхронизирован между всеми
	// клиентами per docs).
	[Sync( SyncFlags.FromHost )] public float TransitionStartedAt { get; set; } = 0f;

	private float _respawnAt = 0f;
	private bool? _lastInScope;
	private List<ModelRenderer> _cachedRenderers;
	private List<Collider> _cachedColliders;
	private bool? _lastCollidersEnabled;

	// Исходная мировая ориентация — сюда возвращаемся после респавна.
	private Rotation _originalWorldRot;
	private bool _originalCaptured = false;

	// True если это шаблон (placed в редакторе, OwnerPlayerId == Empty). Шаблоны
	// невидимы для всех, не отвечают на стрельбу — служат только источником
	// клонов на хосте. Определяется в OnStart, дальше не меняется.
	private bool _isTemplate = false;

	protected override void OnStart()
	{
		// Авто-привязка к тир-зоне (если ещё не задано вручную).
		if ( RoomId == System.Guid.Empty && TirZone.Instance != null )
			RoomId = TirZone.TirRoomId;

		_originalWorldRot = GameObject.WorldRotation;
		_originalCaptured = true;

		// Если OwnerPlayerId не выставлен (== Empty) к моменту OnStart — это
		// scene-placed шаблон. Сразу и навсегда отключаем все его рендеры/
		// коллайдеры. Сам компонент остаётся живым, чтобы хост мог использовать
		// его как источник позиции/ротации для клонов.
		if ( OwnerPlayerId == System.Guid.Empty )
		{
			_isTemplate = true;
			foreach ( var r in Components.GetAll<ModelRenderer>(
				FindMode.EverythingInSelfAndDescendants ) )
				if ( r != null && r.IsValid ) r.Enabled = false;
			foreach ( var c in Components.GetAll<Collider>(
				FindMode.EverythingInSelfAndDescendants ) )
				if ( c != null && c.IsValid ) c.Enabled = false;
		}
	}

	protected override void OnUpdate()
	{
		// Шаблоны полностью пассивны — никаких апдейтов, рендеров, коллайдеров.
		if ( _isTemplate ) return;

		// Хост: респавн по таймеру.
		if ( Networking.IsHost && Hidden && Time.Now >= _respawnAt )
		{
			Hidden = false;
			TransitionStartedAt = Time.Now;
		}

		bool inScope = GameManager.IsInLocalScope( RoomId );

		// Видимость: только для локального владельца.
		bool visibleForLocal = inScope && IsOwnedByLocal();

		// Рендеры — включены ВСЕГДА когда в скоупе и мишень наша (даже падающая,
		// чтобы видеть анимацию). Поднимается выше под visibleForLocal.
		if ( _lastInScope != visibleForLocal )
		{
			_lastInScope = visibleForLocal;
			if ( _cachedRenderers == null )
				_cachedRenderers = Components.GetAll<ModelRenderer>(
					FindMode.EverythingInSelfAndDescendants ).ToList();
			foreach ( var r in _cachedRenderers )
				if ( r != null && r.IsValid && r.Enabled != visibleForLocal ) r.Enabled = visibleForLocal;
		}

		// Коллайдеры — включены только когда мишень видима локальному игроку И
		// она стоит (не Hidden). Для чужих игроков мишень полностью неактивна
		// (триггер не сработает, raycast не найдёт).
		bool collidersOn = visibleForLocal && !Hidden;
		if ( _lastCollidersEnabled != collidersOn )
		{
			_lastCollidersEnabled = collidersOn;
			if ( _cachedColliders == null )
				_cachedColliders = Components.GetAll<Collider>(
					FindMode.EverythingInSelfAndDescendants ).ToList();
			foreach ( var c in _cachedColliders )
				if ( c != null && c.IsValid && c.Enabled != collidersOn ) c.Enabled = collidersOn;
		}

		// Анимация — крутится ТОЛЬКО на хосте (он owner клона). Хост стримит
		// WorldRotation всем клиентам через NetworkTransform — non-host
		// получит анимацию автоматически с интерполяцией. Раньше анимация
		// гейтилась `visibleForLocal`, но для не-своих целей хоста это было
		// false (хост не в тире), поэтому хост не двигал WorldRotation и
		// клиенты получали "стоп". См. документацию:
		//   https://sbox.game/dev/doc/systems/networking-multiplayer/ownership/
		//   "Their position and variables are all controlled by the owner."
		if ( Networking.IsHost && _originalCaptured )
		{
			float elapsed  = TransitionStartedAt > 0f ? Time.Now - TransitionStartedAt : 1f;
			float progress = System.Math.Clamp( elapsed * AnimationSpeed, 0f, 1f );

			// Early-out: анимация уже завершилась и состояние стабильно — нет
			// смысла каждый кадр писать WorldRotation, это просто гонит idle
			// transform-снапшоты по сети для всех клонов всех игроков. Пишем
			// один финальный кадр после завершения (через _wasAnimating-флаг)
			// чтобы клиенты получили точную финальную ротацию, и затем тишина
			// до следующего state change (Hidden flip → TransitionStartedAt
			// сменится → elapsed станет маленьким → progress < 1 → снова пишем).
			bool isAnimating = progress < 1f;
			if ( !isAnimating && !_wasAnimating ) return;
			_wasAnimating = isAnimating;

			Vector3 axis = FallAxisSynced.LengthSquared > 0.01f
				? FallAxisSynced.Normal
				: Vector3.Right;
			Rotation fallenRot = Rotation.FromAxis( axis, FallAngle ) * _originalWorldRot;

			Rotation target = Hidden
				? Rotation.Lerp( _originalWorldRot, fallenRot, progress )   // падение
				: Rotation.Lerp( fallenRot, _originalWorldRot, progress );  // подъём

			GameObject.WorldRotation = target;
		}
	}

	// Отслеживание состояния анимации между кадрами — для idle-throttle (см. OnUpdate).
	private bool _wasAnimating = true;

	/// <summary>Принадлежит ли эта мишень локальному игроку.</summary>
	private bool IsOwnedByLocal()
	{
		if ( OwnerPlayerId == System.Guid.Empty ) return false;
		var local = GameManager.LocalPlayer;
		if ( local?.GameObject == null ) return false;
		return local.GameObject.Id == OwnerPlayerId;
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( Hidden ) return;
		if ( !Networking.IsHost ) return;

		var p = other.GameObject.Root.GetComponent<PlayerStats>();
		if ( p == null ) return;
		if ( p.CurrentRoomId != RoomId ) return;
		// Per-player мишень: засчитываем только своему владельцу.
		if ( !IsOwnedBy( p ) ) return;

		ApplyHit( p );
	}

	/// <summary>
	/// Применяет хит от выстрела. ХОСТ-ONLY. Вызывается напрямую из
	/// PlayerStats.NotifyTirHit RPC (там валидируется владелец-стрелок).
	/// Не [Rpc] — RPC должен идти от объекта которым владеет caller; бот
	/// не владеет мишенью, поэтому маршрутизация идёт через его PlayerStats.
	/// </summary>
	public void HostApplyShot( PlayerStats shooter )
	{
		if ( !Networking.IsHost ) return;
		if ( Hidden ) return;
		if ( shooter == null ) return;
		if ( shooter.CurrentRoomId != RoomId ) return;
		if ( !IsOwnedBy( shooter ) ) return;
		ApplyHit( shooter );
	}

	/// <summary>Принадлежит ли мишень указанному игроку.</summary>
	public bool IsOwnedBy( PlayerStats p )
	{
		if ( OwnerPlayerId == System.Guid.Empty ) return false;
		if ( p?.GameObject == null ) return false;
		return p.GameObject.Id == OwnerPlayerId;
	}

	private void ApplyHit( PlayerStats shooter )
	{
		Hidden = true;
		TransitionStartedAt = Time.Now;
		_respawnAt = Time.Now + RespawnDelay;

		// Считаем ось падения из направления выстрела: горизонтальный вектор
		// от стрелка к мишени → перпендикуляр в горизонтальной плоскости.
		// Это гарантирует что мишень падает СТРОГО назад от того кто стрелял.
		// Cross(Up, dir) даёт ось такую что при FallAngle=+90 верх наклоняется
		// в сторону `dir` — т.е. дальше от стрелка.
		if ( shooter != null )
		{
			var toTarget = GameObject.WorldPosition - shooter.GameObject.WorldPosition;
			toTarget.z = 0;
			if ( toTarget.LengthSquared > 0.01f )
			{
				var dir = toTarget.Normal;
				FallAxisSynced = Vector3.Cross( Vector3.Up, dir ).Normal;
			}
		}

		if ( !string.IsNullOrEmpty( HitSound ) )
			BroadcastHitSound( GameObject.WorldPosition, HitSound );
	}

	void ITriggerListener.OnTriggerExit( Collider other ) { }

	[Rpc.Broadcast]
	private void BroadcastHitSound( Vector3 pos, string soundName )
	{
		if ( !string.IsNullOrEmpty( soundName ) )
			Sound.Play( soundName, pos );
	}
}
