using Sandbox;

public sealed class PlayerInventory : Component
{
	public const int SlotWeapon = 1;
	public const int SlotCoins  = 2;
	public const int SlotEmpty  = 3;

	// Synced so other clients know which item model to render on this player.
	[Property, Sync] public int ActiveSlot { get; set; } = SlotEmpty;

	// Input action names — these must be bound in Project → Input Actions.
	[Property] public string Slot1Action { get; set; } = "Slot1";
	[Property] public string Slot2Action { get; set; } = "Slot2";
	[Property] public string Slot3Action { get; set; } = "Slot3";

	private PlayerStats _stats;
	private PlayerStats.PlayerRole _lastKnownRole;

	protected override void OnStart()
	{
		_stats = Components.Get<PlayerStats>();

		if ( !IsProxy )
		{
			_lastKnownRole = _stats?.Role ?? PlayerStats.PlayerRole.Innocent;
			SetDefaultSlotForRole();
		}
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || _stats == null ) return;

		if ( _stats.IsDead )
		{
			ActiveSlot = SlotEmpty;
			return;
		}

		// React to role changes (e.g. Innocent promoted to Detective).
		if ( _stats.Role != _lastKnownRole )
		{
			_lastKnownRole = _stats.Role;
			SetDefaultSlotForRole();
		}

		// Слоты теперь всегда переключаются свободно — пустой слот = «пустые руки».
		// Когда в пустой выбранный слот прилетает предмет (монетка / пушка), он
		// автоматически окажется в руках, потому что ItemVisibilityController
		// смотрит на ActiveSlot + наличие контента.
		if ( Input.Pressed( Slot1Action ) ) TrySwitch( SlotWeapon );
		else if ( Input.Pressed( Slot2Action ) ) TrySwitch( SlotCoins );
		else if ( Input.Pressed( Slot3Action ) ) TrySwitch( SlotEmpty );

		// Колёсико мыши: прокрутка вверх → предыдущий слот, вниз → следующий.
		var wheel = Input.MouseWheel;
		if ( wheel.y > 0.1f ) CycleSlot( -1 );
		else if ( wheel.y < -0.1f ) CycleSlot( +1 );
	}

	private void CycleSlot( int dir )
	{
		int[] slots = { SlotWeapon, SlotCoins, SlotEmpty };
		int cur = System.Array.IndexOf( slots, ActiveSlot );
		if ( cur < 0 ) cur = 0;
		cur = (cur + dir + slots.Length) % slots.Length;
		TrySwitch( slots[cur] );
	}

	private void SetDefaultSlotForRole()
	{
		// Все начинают с пустыми руками — убийца сам достаёт нож нажав слот 1.
		ActiveSlot = SlotEmpty;
	}

	private void TrySwitch( int slot )
	{
		if ( slot == ActiveSlot ) return;
		// Любой слот теперь можно выбрать, даже если он пустой.
		ActiveSlot = slot;
	}

	public bool HasWeaponForSlot1()
	{
		if ( _stats == null ) return false;
		if ( _stats.Role == PlayerStats.PlayerRole.Murderer ) return true; // always has knife
		return _stats.HasWeapon; // Detective / armed Innocent
	}

	public bool HasCoinsForSlot2()
	{
		return _stats != null && _stats.Coins > 0;
	}
}
