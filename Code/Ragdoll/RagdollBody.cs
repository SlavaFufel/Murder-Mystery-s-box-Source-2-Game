using Sandbox;
using System.Linq;

/// <summary>
/// Physical corpse left behind when a player dies. A key clue in Murder Mystery —
/// it's a persistent body everyone can see and walk up to.
/// The prefab must have: SkinnedModelRenderer (citizen model) + ModelPhysics (ragdoll).
/// </summary>
public sealed class RagdollBody : Component
{
	public static System.Collections.Generic.List<RagdollBody> All { get; } = new();

	[Sync] public string VictimName { get; set; } = "";
	[Sync] public int VictimRole { get; set; } = 0; // PlayerStats.PlayerRole as int

	// К какой GameRoom принадлежит этот труп. Выставляется хостом в момент
	// SpawnRagdoll (PlayerStats), по этому id клиенты понимают: симулировать
	// ли физику и держать ли видимым тело. Регдоллы чужих рум — отключаем.
	[Sync] public System.Guid RoomId { get; set; } = System.Guid.Empty;

	[Property] public SkinnedModelRenderer BodyRenderer { get; set; }

	// Optional linear velocity applied at spawn (e.g. hit direction).
	[Sync] public Vector3 SpawnImpulse { get; set; } = Vector3.Zero;

	// Насколько долго регдолл «живой» физически после спавна. После этого
	// тело замораживается, и игроки не могут его растаскивать по карте.
	[Property] public float SettleTime { get; set; } = 2f;

	private bool _impulseApplied = false;
	private bool _frozen = false;
	private float _age = 0f;
	private bool? _lastInScope;
	private GameRoom _cachedRoom;
	private ModelPhysics _cachedPhys;

	private ModelPhysics GetCachedPhys()
	{
		if ( _cachedPhys == null || !_cachedPhys.IsValid )
			_cachedPhys = Components.Get<ModelPhysics>( FindMode.EverythingInSelfAndDescendants );
		return _cachedPhys;
	}

	protected override void OnEnabled()
	{
		if ( !All.Contains( this ) ) All.Add( this );
	}

	protected override void OnDisabled()
	{
		All.Remove( this );
	}

	protected override void OnStart()
	{
		if ( BodyRenderer == null )
			BodyRenderer = Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );
	}

	private GameRoom FindOwningRoom()
	{
		if ( _cachedRoom != null && _cachedRoom.IsValid ) return _cachedRoom;
		if ( RoomId == System.Guid.Empty ) return null;
		foreach ( var r in Scene.GetAllComponents<GameRoom>() )
			if ( r.GameObject.Id == RoomId ) { _cachedRoom = r; return r; }
		return null;
	}

	protected override void OnUpdate()
	{
		// Host cleans up corpses when the round of the ragdoll's own room ends.
		// Раньше тут был gm.CurrentState — но это состояние локального игрока
		// (хоста), не румы трупа. Теперь смотрим на конкретную руму через RoomId.
		if ( Networking.IsHost )
		{
			var room = FindOwningRoom();
			if ( room != null && room.State != GameRoom.GameState.Playing )
			{
				GameObject.Destroy();
				return;
			}
			// Fallback для старых регдоллов без RoomId — старая семантика.
			if ( room == null && RoomId == System.Guid.Empty )
			{
				var gm = GameManager.Instance;
				if ( gm != null && gm.CurrentState != GameManager.GameState.Playing )
				{
					GameObject.Destroy();
					return;
				}
			}
		}

		// Room scope: тела чужих рум полностью замораживаем (физика + рендер).
		bool inScope = GameManager.IsInLocalScope( RoomId );
		if ( _lastInScope != inScope )
		{
			_lastInScope = inScope;
			if ( BodyRenderer != null && BodyRenderer.Enabled != inScope )
				BodyRenderer.Enabled = inScope;
			var physScope = GetCachedPhys();
			if ( physScope != null )
			{
				bool wantPhys = inScope && !_frozen;
				if ( physScope.Enabled != wantPhys ) physScope.Enabled = wantPhys;
			}
		}
		if ( !inScope ) return;

		// Замороженный труп уже не меняется — пропускаем impulse/settle блок.
		if ( _frozen ) return;

		var phys = GetCachedPhys();

// PhysicsGroup помечен как устаревший в новом API s&box, но альтернативы для
// доступа к отдельным PhysicsBody через ModelPhysics пока нет — используем
// pragma чтобы убрать предупреждение, не ломая работу.
#pragma warning disable CS0618

		// Apply spawn impulse once physics are ready.
		// Заодно добавляем демпфирование и ограничиваем скорость — предотвращает
		// «улёт» тела от проникновений геометрии или чрезмерного импульса.
		if ( !_impulseApplied && phys != null && phys.PhysicsGroup != null )
		{
			const float maxSpeed = 250f;
			foreach ( var body in phys.PhysicsGroup.Bodies )
			{
				if ( SpawnImpulse.LengthSquared > 0.01f )
					body.Velocity += SpawnImpulse;
				body.LinearDamping  = 4f;
				body.AngularDamping = 4f;
				if ( body.Velocity.LengthSquared > maxSpeed * maxSpeed )
					body.Velocity = body.Velocity.Normal * maxSpeed;
			}
			_impulseApplied = true;
		}

		// Через SettleTime замораживаем тело — игроки больше не смогут его двигать.
		_age += Time.Delta;
		if ( !_frozen && _age >= SettleTime && phys != null )
		{
			if ( phys.PhysicsGroup != null )
			{
				foreach ( var body in phys.PhysicsGroup.Bodies )
				{
					try
					{
						body.Velocity        = Vector3.Zero;
						body.AngularVelocity = Vector3.Zero;
						body.MotionEnabled   = false;
						body.GravityEnabled  = false;
					}
					catch { }
				}
			}

			try { phys.Enabled = false; } catch { }
			_frozen = true;
		}

#pragma warning restore CS0618
	}
}
