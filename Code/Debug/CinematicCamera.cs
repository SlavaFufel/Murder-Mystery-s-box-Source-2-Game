using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Админская дебаг-камера для съёмки красивых пролётов.
/// Не зависит от роли/смерти — включается командой mm_cam, замораживает
/// персонажа и отдаёт управление Scene.Camera.
///
/// Команды (только админ, см. AdminList):
///   mm_cam                    — вкл/выкл свободную камеру
///   mm_cam_speed &lt;N&gt;          — базовая скорость (default 600)
///   mm_cam_fov   &lt;N&gt;          — FOV (default 70, 0 = вернуть дефолт)
///   mm_cam_wp_add             — добавить waypoint в текущей точке камеры
///   mm_cam_wp_pop             — удалить последний waypoint
///   mm_cam_wp_clear           — очистить все waypoint'ы
///   mm_cam_wp_list            — распечатать сохранённые точки
///   mm_cam_play  [сек]        — пролёт через все waypoint'ы за N секунд (default 8)
///   mm_cam_stop               — остановить пролёт
///   mm_cam_loop               — переключить зацикленный пролёт
///
/// Управление в режиме камеры:
///   WASD                — движение
///   Space / Ctrl        — вверх / вниз
///   Shift               — ускорение ×3
///   Alt                 — замедление ×0.25 (для микро-движений)
///   Mouse wheel         — менять FOV на лету
///   Mouse               — поворот
/// </summary>
public sealed class CinematicCamera : Component
{
	public static CinematicCamera Instance { get; private set; }

	[Property] public float MoveSpeed { get; set; } = 600f;
	[Property] public float BoostMultiplier { get; set; } = 3f;
	[Property] public float SlowMultiplier { get; set; } = 0.25f;
	[Property] public float MouseSensitivity { get; set; } = 0.1f;
	[Property] public float Fov { get; set; } = 70f;

	// Сохранённые точки и ориентации для пролёта.
	private readonly List<Vector3> _wpPos = new();
	private readonly List<Rotation> _wpRot = new();
	private readonly List<float> _wpFov = new();

	// Воспроизведение.
	private bool _playing;
	private float _playTime;
	private float _playDuration;
	private bool _loop;

	// Состояние ввода / угла.
	private Angles _viewAngles;

	// Замороженный персонаж.
	private PlayerStats _stats;
	private Sandbox.PlayerController _controller;
	private Rigidbody _body;
	private float _origFov;
	private bool _hadOrigFov;

	// Скрытые на время съёмки UI-панели — запоминаем, чтобы восстановить.
	private readonly List<ScreenPanel> _hiddenUi = new();

	protected override void OnAwake()
	{
		Instance = this;
		_stats = Components.Get<PlayerStats>();
		_controller = Components.Get<Sandbox.PlayerController>();
		_body = Components.Get<Rigidbody>();

		var cam = Scene.Camera;
		if ( cam != null )
		{
			_viewAngles = cam.WorldRotation.Angles();
			_origFov = cam.FieldOfView;
			_hadOrigFov = true;
			cam.FieldOfView = Fov;
		}

		FreezeCharacter( true );
		HideAllUi();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
		FreezeCharacter( false );
		var cam = Scene.Camera;
		if ( cam != null && _hadOrigFov ) cam.FieldOfView = _origFov;
		RestoreUi();
	}

	private void HideAllUi()
	{
		_hiddenUi.Clear();
		foreach ( var p in Scene.GetAllComponents<ScreenPanel>() )
		{
			if ( p == null || !p.Enabled ) continue;
			_hiddenUi.Add( p );
			p.Enabled = false;
		}
	}

	private void RestoreUi()
	{
		foreach ( var p in _hiddenUi )
		{
			if ( p.IsValid() ) p.Enabled = true;
		}
		_hiddenUi.Clear();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		var cam = Scene.Camera;
		if ( cam == null ) return;

		if ( _playing )
		{
			TickPlayback( cam );
			return;
		}

		// ── Свободный полёт ───────────────────────────────────────────────
		_viewAngles.pitch += Input.MouseDelta.y * MouseSensitivity;
		_viewAngles.yaw   -= Input.MouseDelta.x * MouseSensitivity;
		_viewAngles.pitch  = _viewAngles.pitch.Clamp( -89f, 89f );

		_viewAngles.roll = 0f;

		var rot = Rotation.From( _viewAngles );
		cam.WorldRotation = rot;

		Vector3 wish = Vector3.Zero;
		if ( Input.Down( "forward" ) )  wish += rot.Forward;
		if ( Input.Down( "backward" ) ) wish -= rot.Forward;
		if ( Input.Down( "left" ) )     wish -= rot.Right;
		if ( Input.Down( "right" ) )    wish += rot.Right;
		if ( Input.Down( "jump" ) )     wish += Vector3.Up;
		if ( Input.Down( "duck" ) )     wish -= Vector3.Up;

		float speed = MoveSpeed;
		if ( Input.Down( "run" ) )     speed *= BoostMultiplier;
		if ( Input.Down( "walk" ) )    speed *= SlowMultiplier;

		if ( wish.LengthSquared > 0f )
			cam.WorldPosition += wish.Normal * speed * Time.Delta;

		// Колесо мыши — FOV.
		float wheel = Input.MouseWheel.y;
		if ( wheel != 0f )
		{
			Fov = (Fov - wheel * 2f).Clamp( 10f, 120f );
			cam.FieldOfView = Fov;
		}
	}

	private void TickPlayback( CameraComponent cam )
	{
		if ( _wpPos.Count < 2 ) { _playing = false; return; }

		_playTime += Time.Delta;
		float t = (_playDuration <= 0f) ? 1f : (_playTime / _playDuration);
		if ( t >= 1f )
		{
			if ( _loop ) { _playTime = 0f; t = 0f; }
			else
			{
				cam.WorldPosition = _wpPos[^1];
				cam.WorldRotation = _wpRot[^1];
				cam.FieldOfView   = _wpFov[^1];
				_playing = false;
				Log.Info( "[mm_cam] Пролёт завершён." );
				return;
			}
		}

		// Easing in/out для плавного старта/остановки.
		float te = SmoothStep( t );
		// Параметризуем по сегментам.
		int segCount = _wpPos.Count - 1;
		float scaled = te * segCount;
		int seg = (int)scaled.Floor().Clamp( 0, segCount - 1 );
		float local = scaled - seg;

		var p0 = _wpPos[(seg - 1).Clamp( 0, segCount )];
		var p1 = _wpPos[seg];
		var p2 = _wpPos[seg + 1];
		var p3 = _wpPos[(seg + 2).Clamp( 0, segCount )];

		cam.WorldPosition = CatmullRom( p0, p1, p2, p3, local );
		cam.WorldRotation = Rotation.Slerp( _wpRot[seg], _wpRot[seg + 1], local );
		cam.FieldOfView   = MathX.Lerp( _wpFov[seg], _wpFov[seg + 1], local );
	}

	private static float SmoothStep( float x )
	{
		x = x.Clamp( 0f, 1f );
		return x * x * (3f - 2f * x);
	}

	private static Vector3 CatmullRom( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		float t2 = t * t;
		float t3 = t2 * t;
		return 0.5f * (
			(2f * p1) +
			(-p0 + p2) * t +
			(2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
			(-p0 + 3f * p1 - 3f * p2 + p3) * t3
		);
	}

	private void FreezeCharacter( bool frozen )
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

	// ── API для команд ────────────────────────────────────────────────────

	public void AddWaypoint()
	{
		var cam = Scene.Camera;
		if ( cam == null ) return;
		_wpPos.Add( cam.WorldPosition );
		_wpRot.Add( cam.WorldRotation );
		_wpFov.Add( cam.FieldOfView );
		Log.Info( $"[mm_cam] Waypoint #{_wpPos.Count} @ {cam.WorldPosition}" );
	}

	public void PopWaypoint()
	{
		if ( _wpPos.Count == 0 ) { Log.Info( "[mm_cam] Нет waypoint'ов." ); return; }
		_wpPos.RemoveAt( _wpPos.Count - 1 );
		_wpRot.RemoveAt( _wpRot.Count - 1 );
		_wpFov.RemoveAt( _wpFov.Count - 1 );
		Log.Info( $"[mm_cam] Удалён последний. Осталось: {_wpPos.Count}" );
	}

	public void ClearWaypoints()
	{
		_wpPos.Clear(); _wpRot.Clear(); _wpFov.Clear();
		Log.Info( "[mm_cam] Все waypoint'ы очищены." );
	}

	public void ListWaypoints()
	{
		if ( _wpPos.Count == 0 ) { Log.Info( "[mm_cam] (пусто)" ); return; }
		for ( int i = 0; i < _wpPos.Count; i++ )
			Log.Info( $"[mm_cam] [{i}] pos={_wpPos[i]}  fov={_wpFov[i]:F1}" );
	}

	public void Play( float duration )
	{
		if ( _wpPos.Count < 2 ) { Log.Warning( "[mm_cam] Нужно минимум 2 waypoint'а." ); return; }
		_playDuration = duration > 0f ? duration : 8f;
		_playTime = 0f;
		_playing = true;
		Log.Info( $"[mm_cam] Пролёт через {_wpPos.Count} точек за {_playDuration:F1} сек. {(_loop ? "(loop)" : "")}" );
	}

	public void Stop()
	{
		if ( !_playing ) return;
		_playing = false;
		Log.Info( "[mm_cam] Пролёт остановлен." );
	}

	public void ToggleLoop()
	{
		_loop = !_loop;
		Log.Info( $"[mm_cam] Loop = {_loop}" );
	}

	// ── ConCmd ────────────────────────────────────────────────────────────

	private static PlayerStats LocalStats()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return null;
		return scene.GetAllComponents<PlayerStats>().FirstOrDefault( p => !p.IsProxy );
	}

	private static bool RequireAdmin()
	{
		if ( AdminList.IsLocalAdmin() ) return true;
		Log.Warning( "[mm_cam] Эту команду может использовать только админ (см. AdminList.cs)." );
		return false;
	}

	[ConCmd( "mm_cam" )]
	public static void Toggle()
	{
		if ( !RequireAdmin() ) return;

		if ( Instance != null )
		{
			Instance.GameObject.Components.Get<CinematicCamera>()?.Destroy();
			Log.Info( "[mm_cam] Кинематографическая камера выключена." );
			return;
		}

		var p = LocalStats();
		if ( p == null ) { Log.Warning( "[mm_cam] Нет локального игрока." ); return; }
		p.GameObject.Components.Create<CinematicCamera>();
		Log.Info( "[mm_cam] Камера активна. WASD/Space/Ctrl — движение, Shift/Alt — скорость, Q/E — крен, колесо — FOV." );
	}

	[ConCmd( "mm_cam_speed" )]
	public static void SetSpeed( float v )
	{
		if ( !RequireAdmin() ) return;
		if ( Instance == null ) { Log.Warning( "[mm_cam] Сначала включи mm_cam." ); return; }
		Instance.MoveSpeed = v.Clamp( 1f, 10000f );
		Log.Info( $"[mm_cam] Speed = {Instance.MoveSpeed}" );
	}

	[ConCmd( "mm_cam_fov" )]
	public static void SetFov( float v )
	{
		if ( !RequireAdmin() ) return;
		if ( Instance == null ) { Log.Warning( "[mm_cam] Сначала включи mm_cam." ); return; }
		var cam = Instance.Scene.Camera;
		if ( cam == null ) return;
		if ( v <= 0f ) v = 70f;
		Instance.Fov = v.Clamp( 10f, 120f );
		cam.FieldOfView = Instance.Fov;
		Log.Info( $"[mm_cam] FOV = {Instance.Fov}" );
	}

	[ConCmd( "mm_cam_wp_add" )]
	public static void WpAdd()
	{
		if ( !RequireAdmin() ) return;
		if ( Instance == null ) { Log.Warning( "[mm_cam] Сначала включи mm_cam." ); return; }
		Instance.AddWaypoint();
	}

	[ConCmd( "mm_cam_wp_pop" )]
	public static void WpPop()
	{
		if ( !RequireAdmin() ) return;
		Instance?.PopWaypoint();
	}

	[ConCmd( "mm_cam_wp_clear" )]
	public static void WpClear()
	{
		if ( !RequireAdmin() ) return;
		Instance?.ClearWaypoints();
	}

	[ConCmd( "mm_cam_wp_list" )]
	public static void WpList()
	{
		if ( !RequireAdmin() ) return;
		Instance?.ListWaypoints();
	}

	[ConCmd( "mm_cam_play" )]
	public static void CmdPlay( float duration = 8f )
	{
		if ( !RequireAdmin() ) return;
		if ( Instance == null ) { Log.Warning( "[mm_cam] Сначала включи mm_cam." ); return; }
		Instance.Play( duration );
	}

	[ConCmd( "mm_cam_stop" )]
	public static void StopPlay()
	{
		if ( !RequireAdmin() ) return;
		Instance?.Stop();
	}

	[ConCmd( "mm_cam_loop" )]
	public static void Loop()
	{
		if ( !RequireAdmin() ) return;
		Instance?.ToggleLoop();
	}
}
