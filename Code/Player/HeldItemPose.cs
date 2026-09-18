using Sandbox;
using Sandbox.Citizen;

/// <summary>
/// Управляет позой «держания предмета» у citizen-модели через
/// CitizenAnimationHelper. Сама модель тела автоматически поднимает руки
/// в нужную позу, в зависимости от того что в активном слоте инвентаря.
///
/// Прицепи на корень Player prefab (рядом с PlayerStats / PlayerInventory).
/// CitizenAnimationHelper должен висеть на body GameObject (там где
/// SkinnedModelRenderer с citizen моделью).
/// </summary>
public sealed class HeldItemPose : Component
{
	[Property] public CitizenAnimationHelper AnimHelper { get; set; }

	private PlayerInventory _inventory;
	private PlayerStats _stats;

	protected override void OnStart()
	{
		_inventory = Components.Get<PlayerInventory>();
		_stats     = Components.Get<PlayerStats>();
		if ( AnimHelper == null )
			AnimHelper = Components.Get<CitizenAnimationHelper>( FindMode.EverythingInSelfAndDescendants );
	}

	protected override void OnUpdate()
	{
		if ( AnimHelper == null || _inventory == null || _stats == null ) return;

		// Scope check (#4): пропускаем анимационную позу для прокси из чужих рум.
		if ( _stats.IsProxy && !GameManager.IsInLocalScope( _stats.CurrentRoomId ) ) return;

		// ВАЖНО: НЕ трогаем IsGrounded / Velocity / WishVelocity. Sandbox.PlayerController
		// сам корректно драйвит CitizenAnimationHelper — на owner'е и (через transform-sync)
		// на прокси. Раньше тут была попытка перехватить velocity из transform-дельты для
		// прокси, но это конфликтовало с встроенным драйвом контроллера и давало другие
		// артефакты («бег на месте»). Корень исходной кривой позы был в другом —
		// per-frame ApplyClothing watch в PlayerStats.OnUpdate сбивал animator state.

		// Мёртвый — никаких поз.
		if ( _stats.IsDead )
		{
			AnimHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
			return;
		}

		int slot = _inventory.ActiveSlot;
		bool isMurderer = _stats.Role == PlayerStats.PlayerRole.Murderer;
		bool hasGun     = _stats.Role == PlayerStats.PlayerRole.Detective || _stats.HasWeapon;
		bool hasCoins   = _stats.Coins > 0;

		bool slot1 = slot == PlayerInventory.SlotWeapon;
		bool slot2 = slot == PlayerInventory.SlotCoins;

		if ( slot1 && isMurderer )
		{
			// Нож — поза замаха/удара.
			AnimHelper.HoldType   = CitizenAnimationHelper.HoldTypes.Swing;
			AnimHelper.Handedness = CitizenAnimationHelper.Hand.Right;
		}
		else if ( slot1 && hasGun )
		{
			// Пистолет — обычная стрелковая поза.
			AnimHelper.HoldType   = CitizenAnimationHelper.HoldTypes.Pistol;
			AnimHelper.Handedness = CitizenAnimationHelper.Hand.Right;
		}
		else if ( slot2 && hasCoins )
		{
			// Монетка — generic «что-то держу в руке».
			AnimHelper.HoldType   = CitizenAnimationHelper.HoldTypes.HoldItem;
			AnimHelper.Handedness = CitizenAnimationHelper.Hand.Right;
		}
		else
		{
			// Пустые руки.
			AnimHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
		}
	}
}
