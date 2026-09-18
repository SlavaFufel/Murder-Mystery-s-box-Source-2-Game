using Sandbox;
using System.Linq;

/// <summary>
/// Каждый кадр копирует мировой трансформ указанной кости в WorldPosition /
/// WorldRotation своего GameObject'а. Используется для прикрепления предметов
/// (нож/пистолет/монета) к руке citizen-модели, чтобы они следовали за всеми
/// поворотами/анимациями тела.
///
/// Раньше использовался SetParent(bone, false) — но в s&box GameObject,
/// возвращаемый GetBoneObject, не всегда корректно обновляет свой
/// world transform. Поэтому копируем вручную через WorldPosition/Rotation.
/// </summary>
public sealed class BoneFollower : Component
{
	[Property] public SkinnedModelRenderer Target { get; set; }

	// hand_R — стандартная кость кисти правой руки в citizen-rig.
	// Для других моделей подставь нужное имя из инспектора Model.
	[Property] public string BoneName { get; set; } = "hand_R";

	// Дополнительный сдвиг/поворот относительно кости.
	[Property] public Vector3 LocalOffset { get; set; } = Vector3.Zero;
	[Property] public new Angles LocalRotation { get; set; } = new Angles( 0f, 0f, 0f );

	// Для локального игрока в FP компенсируем отставание тела по yaw —
	// тогда нож/пистолет/монета мгновенно следуют за камерой, а не «улетают»
	// в сторону на резком повороте стоя на месте.
	[Property] public bool LockToOwnerYawForLocal { get; set; } = true;

	// Дебаг-флажок для тюнинга TP-позы дочернего предмета. Когда включён,
	// FP-anchor (привязка к камере) у ЛОКАЛЬНОГО игрока отключается —
	// heldItems честно следует за костью hand_R, как у прокси. Это значит, что
	// ThirdPersonPosition на дочернем Pistol/coin будет применяться в той же
	// системе координат, что и у других игроков. Используй вместе с
	// FirstPersonHeldItemOffset.PreviewThirdPersonOnLocal — то что ты видишь
	// у себя совпадёт с тем, что видит прокси.
	// В реальной игре оставляй ВЫКЛЮЧЕННЫМ.
	[Property, Title( "Preview Bone Attach On Local (debug)" )]
	public bool PreviewBoneAttachOnLocal { get; set; } = false;

	// FP-якорь у локального игрока — камера, не кость (избавляет от animgraph-лага
	// hand_R и «улёта» предмета при резком повороте). Для ножей этот режим
	// перебивается компонентом BoneAnchorOverride на самом ноже, который
	// выполняется ПОСЛЕ и возвращает нож на кость.
	[Property, Group( "FP Sway" )] public float IdleBobAmplitude { get; set; } = 0.18f;
	[Property, Group( "FP Sway" )] public float IdleBobSpeed     { get; set; } = 1.6f;
	[Property, Group( "FP Sway" )] public float WalkBobAmplitude { get; set; } = 0.55f;
	[Property, Group( "FP Sway" )] public float WalkBobSpeed     { get; set; } = 9f;
	[Property, Group( "FP Sway" )] public float LookSwayAmount   { get; set; } = 0.10f;

	private float _warnTimer = 0f;
	private bool _foundOnce = false;
	private PlayerStats _stats;
	private Sandbox.PlayerController _ctrl;
	private Rotation _prevCamRot = Rotation.Identity;
	private Vector3 _swayLagVec = Vector3.Zero;

	protected override void OnUpdate()
	{
		// Ленивая инициализация stats — нужна для scope check и yaw-фикса.
		// Сначала ищем в своих предках, затем — в предках Target'а (предметы вроде
		// монет/оружия часто живут отдельной иерархией и держатся к телу через Target).
		if ( _stats == null )
			_stats = Components.Get<PlayerStats>( FindMode.EverythingInSelfAndAncestors );
		if ( _stats == null && Target != null )
			_stats = Target.Components.Get<PlayerStats>( FindMode.EverythingInSelfAndAncestors );

		// Scope check (#4): для прокси-игроков из чужих рум пропускаем дорогой
		// GetBoneObject — это главный источник FPS-просадок при множественных румах.
		if ( _stats != null && _stats.IsProxy && !GameManager.IsInLocalScope( _stats.CurrentRoomId ) )
		{
			HideRenderers( true );
			return;
		}

		// FP-якорь у локального игрока: heldItems привязан к камере + sway.
		// Ножи перебивают это через BoneAnchorOverride. Если включён дебаг
		// PreviewBoneAttachOnLocal ИЛИ у любого ребёнка-предмета сейчас включён
		// PreviewThirdPersonOnLocal (тюнинг TP-позы) — пропускаем FP-anchor,
		// идём в bone-mode, чтобы координаты ThirdPersonPosition совпадали
		// с тем, что увидит прокси.
		bool anyChildPreviewingTp = AnyChildPreviewingThirdPerson();
		if ( LockToOwnerYawForLocal && !PreviewBoneAttachOnLocal && !anyChildPreviewingTp
		     && Game.IsPlaying && _stats != null && !_stats.IsProxy )
		{
			var cam = Scene?.Camera;
			if ( cam != null )
			{
				_foundOnce = true;
				HideRenderers( false );

				if ( _ctrl == null )
					_ctrl = Components.Get<Sandbox.PlayerController>( FindMode.EverythingInSelfAndAncestors );

				var camRot = cam.WorldRotation;

				float speed = 0f;
				try { if ( _ctrl != null ) speed = _ctrl.Velocity.WithZ( 0 ).Length; } catch { }
				float moving = System.Math.Clamp( speed / 200f, 0f, 1f );

				float now = Time.Now;
				float idleY = (float)System.Math.Sin( now * IdleBobSpeed ) * IdleBobAmplitude;
				float idleZ = (float)System.Math.Cos( now * IdleBobSpeed * 0.5f ) * IdleBobAmplitude * 0.6f;
				float walkY = (float)System.Math.Sin( now * WalkBobSpeed ) * WalkBobAmplitude * moving;
				float walkZ = (float)System.Math.Sin( now * WalkBobSpeed * 2f ) * WalkBobAmplitude * 0.5f * moving;

				if ( !_prevCamRot.Equals( default(Rotation) ) )
				{
					var delta = camRot * _prevCamRot.Inverse;
					var deltaAng = delta.Angles();
					_swayLagVec += new Vector3( 0f, -deltaAng.yaw * LookSwayAmount, -deltaAng.pitch * LookSwayAmount );
				}
				_prevCamRot = camRot;
				float decay = 1f - (float)System.Math.Exp( -8f * Time.Delta );
				_swayLagVec = Vector3.Lerp( _swayLagVec, Vector3.Zero, decay );

				var camLocalSway = new Vector3( 0f, idleY + walkY + _swayLagVec.y, idleZ + walkZ + _swayLagVec.z );

				GameObject.WorldRotation = camRot * Rotation.From( LocalRotation );
				GameObject.WorldPosition = cam.WorldPosition + (camRot * (LocalOffset + camLocalSway));
				return;
			}
		}

		if ( Target == null )
		{
			HideRenderers( true );
			Warn( "поле Target не назначено — перетащи SkinnedModelRenderer тела (body)." );
			return;
		}

		// «Будим» модель — иногда Bones подгружаются лениво и GetBoneObject
		// возвращает null до тех пор, пока кто-то не дёрнет .Bones.
		if ( !_foundOnce )
		{
			try { _ = Target.Model?.Bones; } catch { }
		}

		var bone = Target.GetBoneObject( BoneName );
		if ( bone == null )
		{
			// Прячем рендереры, пока кость не найдена — иначе предмет видно
			// в случайной позиции (центр мира или место из префаба).
			HideRenderers( true );
			WarnBoneMissing();
			return;
		}

		_foundOnce = true;
		HideRenderers( false );

		var boneRot = bone.WorldRotation;
		var bonePos = bone.WorldPosition;
		var extraRot = Rotation.From( LocalRotation );

		// Yaw-фикс для локального игрока в FP (#8/#9): тело citizen-модели
		// докручивается к взгляду плавно через animgraph, поэтому hand_R кость
		// отстаёт при резком повороте. Используем Scene.Camera.WorldRotation.Yaw()
		// напрямую — это надёжнее EyeAngles из PlayerController, который может
		// вернуть устаревший yaw после цикла Enabled=false/true.
		// ВАЖНО: пропускаем yaw-fix в preview/manual bone-attach режимах, иначе
		// то, что видит локальный игрок, не совпадёт с тем, что увидит прокси
		// (на прокси yaw-fix вообще не работает по условию !IsProxy ниже).
		if ( LockToOwnerYawForLocal && !PreviewBoneAttachOnLocal && !anyChildPreviewingTp
		     && Game.IsPlaying && _stats != null && !_stats.IsProxy )
		{
			var bodyGo = Target.GameObject;
			if ( bodyGo != null )
			{
				float eyeYaw;
				try
				{
					var cam = Scene?.Camera;
					eyeYaw = cam != null ? cam.WorldRotation.Yaw() : bodyGo.WorldRotation.Yaw();
				}
				catch { eyeYaw = bodyGo.WorldRotation.Yaw(); }

				var targetYaw = Rotation.FromYaw( eyeYaw );
				var bodyYaw   = Rotation.FromYaw( bodyGo.WorldRotation.Yaw() );
				var yawFix    = targetYaw * bodyYaw.Inverse;

				var offset = bonePos - bodyGo.WorldPosition;
				bonePos = bodyGo.WorldPosition + (yawFix * offset);
				boneRot = yawFix * boneRot;
			}
		}

		GameObject.WorldRotation = boneRot * extraRot;
		GameObject.WorldPosition = bonePos + (boneRot * LocalOffset);
	}

	private bool _renderersHidden = false;

	private void HideRenderers( bool hide )
	{
		if ( _renderersHidden == hide ) return;
		_renderersHidden = hide;
		try
		{
			foreach ( var r in Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
				r.Enabled = !hide;
		}
		catch { /* ignore */ }
	}

	// Дебаг-помощник: TRUE если хоть один дочерний предмет сейчас в Preview TP
	// режиме. Используется чтобы автоматически отключить FP-anchor у heldItems
	// при тюнинге TP-позы — тогда то, что ты видишь у себя на экране, совпадает
	// с тем, что увидит прокси, и не нужно вручную дёргать вторую галку.
	private bool AnyChildPreviewingThirdPerson()
	{
		if ( GameObject == null ) return false;
		foreach ( var ch in GameObject.Children )
		{
			if ( ch == null ) continue;
			var off = ch.Components.Get<FirstPersonHeldItemOffset>();
			if ( off != null && off.SupportsThirdPersonOverride && off.PreviewThirdPersonOnLocal )
				return true;
		}
		return false;
	}

	private void Warn( string msg )
	{
		_warnTimer += Time.Delta;
		if ( _warnTimer < 3f ) return;
		_warnTimer = 0f;
		Log.Warning( $"[BoneFollower] '{GameObject.Name}': {msg}" );
	}

	private void WarnBoneMissing()
	{
		_warnTimer += Time.Delta;
		if ( _warnTimer < 3f ) return;
		_warnTimer = 0f;

		// Подскажем пользователю какие кости вообще есть на модели.
		string available = "недоступен";
		try
		{
			var bones = Target.Model?.Bones?.AllBones?
				.Take( 20 )
				.Select( b => b.Name )
				.ToList();
			if ( bones != null && bones.Count > 0 )
				available = string.Join( ", ", bones ) + (bones.Count >= 20 ? " ..." : "");
		}
		catch { }

		Log.Warning( $"[BoneFollower] '{GameObject.Name}': кость '{BoneName}' не найдена. " +
			$"Доступные кости: {available}" );
	}
}
