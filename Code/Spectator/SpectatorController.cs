using Sandbox;
using System.Linq;

/// <summary>
/// Free-fly spectator camera. Activates on the local player when they die or
/// join mid-round as a spectator. Takes over movement + camera while active.
/// </summary>
public sealed class SpectatorController : Component
{
	[Property] public float MoveSpeed { get; set; } = 450f;
	[Property] public float BoostMultiplier { get; set; } = 3f;
	[Property] public float MouseSensitivity { get; set; } = 0.1f;
	// Радиус «барьера» вокруг LobbyPoint комнаты — спектатор не вылетит за него.
	// Совпадает с радиусом, в котором GameRoom считает свои сущности (3000).
	[Property] public float SpectatorRoomRadius { get; set; } = 5000f;

	private PlayerStats _stats;
	private Sandbox.PlayerController _controller;
	private Rigidbody _body;

	private Angles _viewAngles;
	private bool _wasActive = false;

	// Player being followed (null = free cam).
	private PlayerStats _followTarget;
	public PlayerStats FollowTarget => _followTarget;

	protected override void OnStart()
	{
		_stats = Components.Get<PlayerStats>();
		_controller = Components.Get<Sandbox.PlayerController>();
		_body = Components.Get<Rigidbody>();
	}

	public bool IsActive => !IsProxy && _stats != null && (_stats.IsDead || _stats.IsSpectator);

	protected override void OnUpdate()
	{
		if ( IsProxy || _stats == null ) return;

		bool active = IsActive;
		if ( active != _wasActive )
		{
			SetCharacterFrozen( active );
			if ( active )
			{
				_viewAngles = Scene.Camera?.WorldRotation.Angles() ?? default;
				// Снапнуть камеру на текущую позицию игрока — иначе при входе
				// в комнату как наблюдатель камера зависает в хабе.
				var cam = Scene.Camera;
				if ( cam != null )
					cam.WorldPosition = GameObject.WorldPosition + Vector3.Up * 64f;

				// При первом активации в новой комнате — попробовать сразу
				// привязаться к живому игроку, чтобы не появляться «в стене».
				_followTarget = FindNearestLivePlayer();
			}
			_wasActive = active;
		}
		if ( !active ) return;

		HandleInput();
		UpdateCamera();
		ClampToRoom();
	}

	private void SetCharacterFrozen( bool frozen )
	{
		if ( _controller != null ) _controller.Enabled = !frozen;
		if ( _body != null )
		{
			_body.Gravity = !frozen;
			if ( frozen )
			{
				_body.Velocity = Vector3.Zero;
				_body.AngularVelocity = Vector3.Zero;
			}
		}
	}

	private void HandleInput()
	{
		// Cycle follow targets with Jump (Space) / use (E).
		if ( Input.Pressed( "jump" ) ) CycleFollow( +1 );
		if ( Input.Pressed( "reload" ) ) CycleFollow( -1 );
		if ( Input.Pressed( "attack2" ) ) _followTarget = null; // right-click drops to free cam
	}

	private PlayerStats FindNearestLivePlayer()
	{
		var roomId = _stats?.CurrentRoomId ?? System.Guid.Empty;
		return Scene.GetAllComponents<PlayerStats>()
			.Where( p => !p.IsDead && !p.IsSpectator && p != _stats )
			.Where( p => roomId == System.Guid.Empty || p.CurrentRoomId == roomId )
			.OrderBy( p => p.GameObject.WorldPosition.Distance( GameObject.WorldPosition ) )
			.FirstOrDefault();
	}

	private void ClampToRoom()
	{
		// Барьер: спектатор не должен улетать за пределы своей комнаты.
		// Берём LobbyPoint своей комнаты и зажимаем камеру в сфере радиуса.
		var roomId = _stats?.CurrentRoomId ?? System.Guid.Empty;
		if ( roomId == System.Guid.Empty ) return;

		var room = Scene.GetAllComponents<GameRoom>().FirstOrDefault( r => r.GameObject.Id == roomId );
		var center = room?.LobbyPoint?.WorldPosition;
		if ( center == null ) return;

		var cam = Scene.Camera;
		if ( cam == null ) return;

		var delta = cam.WorldPosition - center.Value;
		float dist = delta.Length;
		if ( dist > SpectatorRoomRadius )
			cam.WorldPosition = center.Value + delta.Normal * SpectatorRoomRadius;
	}

	private void CycleFollow( int dir )
	{
		// Фильтруем по комнате — спектатор не должен переключаться на игроков
		// в хабе или других румах (#23).
		var roomId = _stats?.CurrentRoomId ?? System.Guid.Empty;
		var alive = Scene.GetAllComponents<PlayerStats>()
			.Where( p => !p.IsDead && !p.IsSpectator && p != _stats )
			.Where( p => roomId == System.Guid.Empty || p.CurrentRoomId == roomId )
			.OrderBy( p => p.GameObject.Id.ToString() )
			.ToList();
		if ( alive.Count == 0 ) { _followTarget = null; return; }

		int idx = _followTarget == null ? -1 : alive.IndexOf( _followTarget );
		idx = (idx + dir + alive.Count) % alive.Count;
		_followTarget = alive[idx];
	}

	private void UpdateCamera()
	{
		var cam = Scene.Camera;
		if ( cam == null ) return;

		if ( _followTarget != null && _followTarget.IsValid() && !_followTarget.IsDead )
		{
			// Third-person over the shoulder of the followed player.
			var target  = _followTarget.GameObject.WorldPosition + Vector3.Up * 72f;
			var desired = target - cam.WorldRotation.Forward * 180f + Vector3.Up * 20f;
			cam.WorldPosition = Vector3.Lerp( cam.WorldPosition, desired, Time.Delta * 6f );

			var lookAt = Rotation.LookAt( target - cam.WorldPosition );
			cam.WorldRotation = Rotation.Slerp( cam.WorldRotation, lookAt, Time.Delta * 6f );
			_viewAngles = cam.WorldRotation.Angles();
			return;
		}

		// Free-fly cam.
		_viewAngles.pitch += Input.MouseDelta.y * MouseSensitivity;
		_viewAngles.yaw   -= Input.MouseDelta.x * MouseSensitivity;
		_viewAngles.pitch = _viewAngles.pitch.Clamp( -89f, 89f );
		_viewAngles.roll  = 0f;

		var rot = Rotation.From( _viewAngles );
		cam.WorldRotation = rot;

		Vector3 wish = Vector3.Zero;
		if ( Input.Down( "forward" ) )  wish += rot.Forward;
		if ( Input.Down( "backward" ) ) wish -= rot.Forward;
		if ( Input.Down( "left" ) )     wish -= rot.Right;
		if ( Input.Down( "right" ) )    wish += rot.Right;
		if ( Input.Down( "jump" ) )     wish += Vector3.Up;
		if ( Input.Down( "duck" ) )     wish -= Vector3.Up;

		if ( wish.LengthSquared > 0f )
		{
			float speed = MoveSpeed * (Input.Down( "run" ) ? BoostMultiplier : 1f);
			cam.WorldPosition += wish.Normal * speed * Time.Delta;
		}
	}
}
