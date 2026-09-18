using Sandbox;

/// <summary>
/// Упрощённая копия BoneFollower специально для группы ножей (heldItemsKnives).
/// Просто копирует мировой трансформ кости hold_R на свой GameObject — без
/// FP-anchor, без bob-sway, без yaw-lock. Это значит: когда citizen-animgraph
/// играет анимацию замаха (Melee_Weapons_2H_Attack_01 через b_attack), кость
/// hand_R/hold_R двигается, а нож честно следует за ней — игрок видит дугу
/// удара в FP.
///
/// Оригинальный BoneFollower остаётся без изменений на heldItems (пистолет,
/// монета) — там нужен sway/yaw-lock для FP-стабильности оружия.
/// </summary>
public sealed class KnifeBoneFollower : Component
{
	[Property] public SkinnedModelRenderer Target { get; set; }
	[Property] public string BoneName { get; set; } = "hold_R";

	[Property] public Vector3 LocalOffset { get; set; } = Vector3.Zero;
	[Property] public new Angles LocalRotation { get; set; } = new Angles( 0f, 0f, 0f );

	private float _warnTimer = 0f;
	private bool _foundOnce = false;

	protected override void OnUpdate()
	{
		if ( Target == null )
		{
			Warn( "поле Target не назначено — перетащи SkinnedModelRenderer тела (body)." );
			return;
		}

		// «Будим» модель — иногда Bones подгружаются лениво.
		if ( !_foundOnce )
		{
			try { _ = Target.Model?.Bones; } catch { }
		}

		var bone = Target.GetBoneObject( BoneName );
		if ( bone == null )
		{
			Warn( $"кость '{BoneName}' не найдена на модели Target." );
			return;
		}

		_foundOnce = true;

		var boneRot = bone.WorldRotation;
		var bonePos = bone.WorldPosition;
		var extraRot = Rotation.From( LocalRotation );

		GameObject.WorldRotation = boneRot * extraRot;
		GameObject.WorldPosition = bonePos + (boneRot * LocalOffset);
	}

	private void Warn( string msg )
	{
		_warnTimer += Time.Delta;
		if ( _warnTimer < 3f ) return;
		_warnTimer = 0f;
		Log.Warning( $"[KnifeBoneFollower] '{GameObject.Name}': {msg}" );
	}
}
