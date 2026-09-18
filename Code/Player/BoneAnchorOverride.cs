using Sandbox;

/// <summary>
/// Перебивает позицию/ротацию своего GameObject, прижимая её к указанной кости
/// SkinnedModelRenderer-а. Нужен для ножей в иерархии heldItems: heldItems сам
/// привязан к камере (BoneFollower.LockToOwnerYawForLocal=true), но для ножа
/// мы хотим оригинальное поведение — следование за hand_R костью, как было
/// до camera-anchor оптимизации. Этот компонент запускается каждый кадр и
/// пишет WorldPosition/Rotation в самом конце update-цикла, поэтому его
/// результат — финальный.
///
/// Привязки задаются через инспектор. По умолчанию ищем кость в первом
/// SkinnedModelRenderer, найденном выше по иерархии (citizen body).
/// </summary>
public sealed class BoneAnchorOverride : Component
{
	[Property] public SkinnedModelRenderer Target { get; set; }
	[Property] public string BoneName { get; set; } = "hold_R";

	private PlayerStats _stats;

	protected override void OnUpdate()
	{
		if ( _stats == null )
			_stats = Components.Get<PlayerStats>( FindMode.EverythingInSelfAndAncestors );

		// Прокси-игроки из чужой румы — пропускаем (их видимость рулится отдельно).
		if ( _stats != null && _stats.IsProxy && !GameManager.IsInLocalScope( _stats.CurrentRoomId ) )
			return;

		if ( Target == null )
			Target = Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndAncestors );
		if ( Target == null ) return;

		var bone = Target.GetBoneObject( BoneName );
		if ( bone == null ) return;

		// LocalPosition/Rotation, которые на ноже уже стоят (FirstPersonHeldItemOffset
		// их крутит для FP/ADS), будут применены ПОВЕРХ кости через WorldPosition/
		// WorldRotation. Чтобы их сохранить, считаем мировой трансформ кости и
		// добавляем к нему текущий локальный offset.
		var bonePos = bone.WorldPosition;
		var boneRot = bone.WorldRotation;
		var localPos = GameObject.LocalPosition;
		var localRot = GameObject.LocalRotation;

		GameObject.WorldRotation = boneRot * localRot;
		GameObject.WorldPosition = bonePos + (boneRot * localPos);
	}
}
