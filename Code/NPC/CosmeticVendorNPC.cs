using Sandbox;
using Sandbox.UI;
using System.Linq;

/// <summary>
/// NPC-торговец косметикой в лобби. Аналог ShopNPC, но открывает CosmeticShopUI
/// и работает за персистентные кристаллы (валидация — внутри PlayerStats RPC).
/// </summary>
public sealed class CosmeticVendorNPC : Component
{
	[Property] public float InteractRange { get; set; } = 150f;
	[Property] public string UseAction { get; set; } = "use";

	/// <summary>
	/// Кейсы, которые продаёт этот NPC. Дизайнер добавляет/удаляет в инспекторе.
	/// Каждый кейс — id, цена, картинка, дроп-пул. См. CaseDefinition.
	/// </summary>
	[Property] public System.Collections.Generic.List<CaseDefinition> Cases { get; set; } = new();

	public static CosmeticVendorNPC ActiveForLocal { get; private set; }

	public bool LocalPlayerInRange { get; private set; }

	// Открыта ли панель магазина (локально). UI читает это поле.
	public static bool IsShopOpen { get; set; } = false;

	protected override void OnUpdate()
	{
		var local = Scene.GetAllComponents<PlayerStats>().FirstOrDefault( x => !x.IsProxy );
		if ( local == null )
		{
			LocalPlayerInRange = false;
			if ( ActiveForLocal == this ) { ActiveForLocal = null; IsShopOpen = false; }
			return;
		}

		float dist = Vector3.DistanceBetween( local.GameObject.WorldPosition, WorldPosition );
		LocalPlayerInRange = dist <= InteractRange;

		if ( LocalPlayerInRange )
		{
			ActiveForLocal = this;

			// Не переключаем магазин, если поверх него открыта анимация открытия кейса.
			if ( Input.Pressed( UseAction ) && !CaseOpeningUI.IsOpenStatic )
				IsShopOpen = !IsShopOpen;
		}
		else if ( ActiveForLocal == this )
		{
			ActiveForLocal = null;
			IsShopOpen = false;
		}
	}
}
