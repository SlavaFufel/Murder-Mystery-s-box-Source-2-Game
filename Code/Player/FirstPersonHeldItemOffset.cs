using Sandbox;

/// <summary>
/// Дополнительный сдвиг/поворот/масштаб предмета в руке, применяемый ТОЛЬКО
/// локальному игроку (т.е. в FP-виде). Не ломает то, как предмет выглядит со
/// стороны — другие игроки видят базовый Local Transform, который выставлен в
/// префабе.
///
/// Дополнительно — отдельный пресет ADS (aim down sights). Когда у PlayerStats
/// IsAds = true и этот предмет — текущий, плавно переходит в ADS-позу.
///
/// Куда вешать: на дочерний GameObject под heldItems (Knife / Pistol / Coin).
/// Базовый трансформ (как лежит со стороны) задаёшь обычным Local Position /
/// Rotation / Scale — этот компонент их не трогает.
/// </summary>
public sealed class FirstPersonHeldItemOffset : Component
{
	[Property, Group( "First-Person" )] public Vector3 FirstPersonPosition { get; set; } = Vector3.Zero;
	[Property, Group( "First-Person" )] public Angles FirstPersonRotation { get; set; } = new Angles( 0, 0, 0 );
	[Property, Group( "First-Person" )] public float FirstPersonScale { get; set; } = 1f;

	[Property, Group( "ADS" )] public bool SupportsAds { get; set; } = false;
	[Property, Group( "ADS" )] public Vector3 AdsPosition { get; set; } = Vector3.Zero;
	[Property, Group( "ADS" )] public Angles AdsRotation { get; set; } = new Angles( 0, 0, 0 );
	[Property, Group( "ADS" )] public float AdsScale { get; set; } = 1f;

	// Третье лицо — как видят твоё оружие ДРУГИЕ игроки (прокси). Раньше для
	// них применялся «голый» Local Transform из префаба, который часто стоял
	// под FP-выравнивание и со стороны выглядел криво. Теперь — отдельный
	// аддитивный пресет: дельта поверх _basePos / _baseRot, как у FP/ADS.
	// SupportsThirdPersonOverride=false → fallback к старому поведению (база
	// без изменений), не ломает пистолет/монету пока ты их не настроил.
	[Property, Group( "Third-Person" )] public bool SupportsThirdPersonOverride { get; set; } = false;
	[Property, Group( "Third-Person" )] public Vector3 ThirdPersonPosition { get; set; } = Vector3.Zero;
	[Property, Group( "Third-Person" )] public Angles ThirdPersonRotation { get; set; } = new Angles( 0, 0, 0 );
	[Property, Group( "Third-Person" )] public float ThirdPersonScale { get; set; } = 1f;

	// Дебаг-флажок для удобной настройки TP-позы. Когда включён, TP-трансформ
	// принудительно применяется и к ЛОКАЛЬНОМУ игроку — т.е. ты сам видишь свой
	// Pistol/Knife так, как его видят другие. Удобно крутить значения вживую.
	// В реальной игре оставляй ВЫКЛЮЧЕННЫМ — иначе локальный FP-вид сломается.
	[Property, Group( "Third-Person" ), Title( "Preview TP On Local (debug)" )]
	public bool PreviewThirdPersonOnLocal { get; set; } = false;

	[Property] public float LerpSpeed { get; set; } = 18f;

	// Синкаем prefab-базу владельца на прокси. Без этого прокси захватывает
	// LocalPosition уже ПОСЛЕ того как сетевой transform-sync переписал её
	// владельцевым FP-значением → база «грязная» → пистолет уезжает.
	// Документация: https://sbox.game/dev/doc/systems/networking-multiplayer/sync-properties/
	[Sync] public Vector3 SyncedBasePos   { get; set; }
	[Sync] public Angles  SyncedBaseRot   { get; set; }
	[Sync] public Vector3 SyncedBaseScale { get; set; } = Vector3.One;
	[Sync] public bool    BaseSynced      { get; set; } = false;

	private PlayerStats _stats;
	private Vector3 _basePos;
	private Rotation _baseRot;
	private Vector3 _baseScale;

	private Vector3 _curPos;
	private Rotation _curRot;
	private Vector3 _curScale;

	// Удар (recoil/swing): кратковременное аддитивное смещение поверх FP/ADS.
	// Триггерится из KnifeWeapon (взмах) и PlayerStats.Attack (отдача пистолета).
	// Затухает по экспоненте за _kickDuration секунд.
	private Vector3 _kickPosFrom = Vector3.Zero;
	private Angles  _kickRotFrom = new Angles( 0, 0, 0 );
	private float   _kickAge = 999f;
	private float   _kickDuration = 0.2f;

	/// <summary>Дёрнуть предмет на кадр-два: pos в локальных осях кости/камеры,
	/// rot — добавочные углы. Используется для отдачи / удара в FP.</summary>
	public void Punch( Vector3 pos, Angles rot, float duration = 0.20f )
	{
		_kickPosFrom = pos;
		_kickRotFrom = rot;
		_kickAge = 0f;
		_kickDuration = System.Math.Max( 0.05f, duration );
		_swingActive = false;
	}

	// Двухключевой взмах через экран (для ножа). Линейно интерполирует pos и rot
	// от start к end за duration секунд; после этого базовый lerp вернёт предмет
	// в нейтральную позу. Получается видимая дуга через всё поле зрения.
	private bool _swingActive = false;
	private Vector3 _swingPosA, _swingPosB;
	private Angles  _swingRotA, _swingRotB;

	public void PunchSwing( Vector3 startPos, Angles startRot, Vector3 endPos, Angles endRot, float duration = 0.30f )
	{
		_swingActive = true;
		_swingPosA = startPos;
		_swingPosB = endPos;
		_swingRotA = startRot;
		_swingRotB = endRot;
		_kickAge = 0f;
		_kickDuration = System.Math.Max( 0.05f, duration );
	}

	protected override void OnAwake()
	{
		_stats = Components.Get<PlayerStats>( FindMode.EverythingInSelfAndAncestors );

		// Базу захватываем в OnAwake — это работает на ВЛАДЕЛЬЦЕ (он первым
		// видит чистый prefab LocalPosition). На прокси значения могут быть
		// «грязными» от сетевого sync-а — поэтому ниже мы дополнительно
		// синкаем правильную базу от владельца через [Sync] поле.
		_basePos   = GameObject.LocalPosition;
		_baseRot   = GameObject.LocalRotation;
		_baseScale = GameObject.LocalScale;
		_curPos    = _basePos;
		_curRot    = _baseRot;
		_curScale  = _baseScale;
	}

	protected override void OnUpdate()
	{
		// Владелец: на первом кадре зафиксировать prefab-базу как [Sync] —
		// прокси-клиенты будут использовать это значение вместо своего
		// захвата, который мог быть «загрязнён» сетевым transform-sync'ом.
		if ( _stats != null && !_stats.IsProxy && !BaseSynced )
		{
			SyncedBasePos   = _basePos;
			SyncedBaseRot   = _baseRot.Angles();
			SyncedBaseScale = _baseScale;
			BaseSynced      = true;
		}

		// Прокси: как только синкнутая база пришла — применяем её. Это
		// перезаписывает «грязное» значение которое мы поймали в OnAwake.
		if ( _stats != null && _stats.IsProxy && BaseSynced )
		{
			_basePos   = SyncedBasePos;
			_baseRot   = Rotation.From( SyncedBaseRot );
			_baseScale = SyncedBaseScale;
		}


		// FP-смещение применяется только локальному игроку в реальной игре.
		// !IsProxy = это игрок текущего клиента (та же проверка, что и в HUD).
		// Game.IsPlaying = false в превью префаба — там показываем базу.
		bool isLocal = Game.IsPlaying && _stats != null && !_stats.IsProxy;

		// Дебаг-режим: насильно показать TP-трансформ локальному игроку, чтобы
		// тюнить значения и сразу видеть результат. Рассматриваем как «прокси».
		bool useThirdPerson = !isLocal || (SupportsThirdPersonOverride && PreviewThirdPersonOnLocal);

		Vector3 targetPos;
		Rotation targetRot;
		Vector3 targetScale;

		if ( useThirdPerson )
		{
			// Сторонние игроки (прокси) или Preview-режим у локального.
			if ( SupportsThirdPersonOverride )
			{
				targetPos = _basePos + ThirdPersonPosition;
				targetRot = _baseRot * Rotation.From( ThirdPersonRotation );
				targetScale = _baseScale * ThirdPersonScale;
			}
			else
			{
				// Фолбэк: оригинальный базовый трансформ из префаба — старое
				// поведение, чтобы пистолет/монета не сломались, пока ты их
				// не настроил через ThirdPersonOverride.
				targetPos = _basePos;
				targetRot = _baseRot;
				targetScale = _baseScale;
			}
		}
		else if ( SupportsAds && _stats.IsAds )
		{
			targetPos = _basePos + AdsPosition;
			targetRot = _baseRot * Rotation.From( AdsRotation );
			targetScale = _baseScale * AdsScale;
		}
		else
		{
			targetPos = _basePos + FirstPersonPosition;
			targetRot = _baseRot * Rotation.From( FirstPersonRotation );
			targetScale = _baseScale * FirstPersonScale;
		}

		float t = 1f - (float)System.Math.Exp( -LerpSpeed * Time.Delta );
		_curPos = Vector3.Lerp( _curPos, targetPos, t );
		_curRot = Rotation.Slerp( _curRot, targetRot, t );
		_curScale = Vector3.Lerp( _curScale, targetScale, t );

		// Поверх — кратковременный «kick» (Punch) или двухключевой взмах (PunchSwing).
		Vector3 kickPos = Vector3.Zero;
		Angles  kickRot = new Angles( 0, 0, 0 );
		if ( _kickAge < _kickDuration )
		{
			_kickAge += Time.Delta;
			float u = System.Math.Clamp( _kickAge / _kickDuration, 0f, 1f );

			if ( _swingActive )
			{
				// Линейный sweep от start к end. После окончания базовый lerp
				// (LerpSpeed) сам вернёт предмет в нейтральную позу.
				kickPos = Vector3.Lerp( _swingPosA, _swingPosB, u );
				kickRot = new Angles(
					_swingRotA.pitch + (_swingRotB.pitch - _swingRotA.pitch) * u,
					_swingRotA.yaw   + (_swingRotB.yaw   - _swingRotA.yaw  ) * u,
					_swingRotA.roll  + (_swingRotB.roll  - _swingRotA.roll ) * u );
			}
			else
			{
				// Squared falloff — резкий старт, плавный конец.
				float kT = 1f - u;
				float kCurve = kT * kT;
				kickPos = _kickPosFrom * kCurve;
				kickRot = new Angles( _kickRotFrom.pitch * kCurve, _kickRotFrom.yaw * kCurve, _kickRotFrom.roll * kCurve );
			}
		}
		else
		{
			_swingActive = false;
		}

		GameObject.LocalPosition = _curPos + kickPos;
		GameObject.LocalRotation = _curRot * Rotation.From( kickRot );
		GameObject.LocalScale = _curScale;
	}
}
