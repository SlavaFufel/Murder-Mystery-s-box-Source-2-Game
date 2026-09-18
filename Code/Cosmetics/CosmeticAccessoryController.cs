using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Управляет видимостью аксессуаров на спине игрока (крылья / плащ).
/// Модели — дочерние GameObject'ы под пустышкой, привязанной BoneFollower-ом
/// к кости спины. Заполняешь BackSkins в инспекторе: Id из CosmeticCatalog
/// (back_none / back_wings_angel / back_cape_red) и соответствующий GameObject.
/// </summary>
public sealed class CosmeticAccessoryController : Component
{
	[Property] public List<ItemVisibilityController.CosmeticSkin> BackSkins { get; set; } = new();

	private PlayerStats _stats;

	protected override void OnStart()
	{
		_stats = Components.Get<PlayerStats>();
	}

	protected override void OnUpdate()
	{
		if ( BackSkins == null || BackSkins.Count == 0 ) return;

		string equipped = _stats?.GetEquipped( "back" ) ?? "back_none";
		bool hideAll = _stats != null && _stats.IsDead;

		foreach ( var s in BackSkins )
		{
			if ( s.Model == null ) continue;
			bool match = !hideAll && s.Id == equipped;
			if ( s.Model.Enabled != match ) s.Model.Enabled = match;
		}
	}
}
