using Sandbox;
using System.Linq;

public sealed class ShopNPC : Component
{
	[Property] public float InteractRange { get; set; } = 150f;
	[Property] public int GunPrice { get; set; } = 10;
	[Property] public string UseAction { get; set; } = "use";
	[Property] public string PurchaseSound { get; set; } = "sounds/shop/purchase.sound";

	// The PlayerStats this shop is currently offering a deal to (local-only UI state).
	public static ShopNPC ActiveShopForLocal { get; private set; }

	public bool LocalPlayerInRange { get; private set; }

	protected override void OnUpdate()
	{
		var local = Scene.GetAllComponents<PlayerStats>().FirstOrDefault( x => !x.IsProxy );
		if ( local == null || local.IsDead )
		{
			LocalPlayerInRange = false;
			if ( ActiveShopForLocal == this ) ActiveShopForLocal = null;
			return;
		}

		float dist = Vector3.DistanceBetween( local.GameObject.WorldPosition, WorldPosition );
		LocalPlayerInRange = dist <= InteractRange;

		if ( LocalPlayerInRange )
		{
			ActiveShopForLocal = this;

			if ( Input.Pressed( UseAction ) )
				RequestPurchase( local );
		}
		else if ( ActiveShopForLocal == this )
		{
			ActiveShopForLocal = null;
		}
	}

	private void RequestPurchase( PlayerStats buyer )
	{
		TryBuyGun( buyer.GameObject.Id );
	}

	// Host validates the purchase authoritatively.
	[Rpc.Broadcast]
	public void TryBuyGun( System.Guid buyerId )
	{
		if ( !Networking.IsHost ) return;

		var buyer = Scene.GetAllComponents<PlayerStats>()
			.FirstOrDefault( p => p.GameObject.Id == buyerId );

		if ( buyer == null || buyer.IsDead ) return;
		if ( buyer.Role == PlayerStats.PlayerRole.Murderer ) return; // murderer can never buy
		if ( buyer.HasWeapon ) return;
		if ( buyer.Coins < GunPrice ) return;

		buyer.SpendCoins( GunPrice );
		buyer.GrantWeapon();

		if ( !string.IsNullOrEmpty( PurchaseSound ) )
			BroadcastPurchaseSound( WorldPosition );

		Log.Info( $"{buyer.GameObject.Name} купил пистолет у торговца." );
	}

	[Rpc.Broadcast]
	private void BroadcastPurchaseSound( Vector3 pos )
	{
		if ( !string.IsNullOrEmpty( PurchaseSound ) )
			Sfx.Play( PurchaseSound, pos );
	}

	public bool CanLocalBuy( PlayerStats local )
	{
		if ( local == null || local.IsDead ) return false;
		if ( local.Role == PlayerStats.PlayerRole.Murderer ) return false;
		if ( local.HasWeapon ) return false;
		return local.Coins >= GunPrice;
	}
}
