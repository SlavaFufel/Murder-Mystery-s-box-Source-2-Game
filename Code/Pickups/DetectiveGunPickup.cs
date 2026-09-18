using Sandbox;
using System.Linq;

public sealed class DetectiveGunPickup : Component
{
	[Property] public float PickupRange { get; set; } = 100f;
	[Property] public string UseAction { get; set; } = "use";

	// К какой руме принадлежит дроп — выставляется хостом в OnDetectiveDied.
	[Sync] public System.Guid RoomId { get; set; } = System.Guid.Empty;

	// Used by the radar UI to find and point at these pickups.
	public static System.Collections.Generic.List<DetectiveGunPickup> All { get; } = new();

	protected override void OnEnabled()
	{
		if ( !All.Contains( this ) ) All.Add( this );
	}

	protected override void OnDisabled()
	{
		All.Remove( this );
	}

	private bool? _lastInScope;
	private System.Collections.Generic.List<ModelRenderer> _cachedRenderers;

	protected override void OnUpdate()
	{
		// Видимость дропа в чужой руме — выключаем рендер.
		bool inScope = GameManager.IsInLocalScope( RoomId );
		if ( _lastInScope != inScope )
		{
			_lastInScope = inScope;
			if ( _cachedRenderers == null )
				_cachedRenderers = Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ).ToList();
			foreach ( var r in _cachedRenderers )
				if ( r != null && r.IsValid && r.Enabled != inScope ) r.Enabled = inScope;
		}

		// Host destroys dropped guns when the round ends.
		if ( Networking.IsHost )
		{
			var gm = GameManager.Instance;
			if ( gm != null && gm.CurrentState != GameManager.GameState.Playing )
			{
				GameObject.Destroy();
				return;
			}
		}

		// Подобрать дроп можно только из своей румы — в чужую даже не считаем дистанцию.
		if ( !inScope ) return;

		var local = Scene.GetAllComponents<PlayerStats>().FirstOrDefault( x => !x.IsProxy );
		if ( local == null || local.IsDead ) return;
		if ( local.Role != PlayerStats.PlayerRole.Innocent ) return;

		float d = Vector3.DistanceBetween( local.GameObject.WorldPosition, WorldPosition );
		if ( d > PickupRange ) return;

		if ( Input.Pressed( UseAction ) )
			RequestPickup( local.GameObject.Id );
	}

	[Rpc.Broadcast]
	public void RequestPickup( System.Guid pickerId )
	{
		if ( !Networking.IsHost ) return;

		var picker = Scene.GetAllComponents<PlayerStats>()
			.FirstOrDefault( p => p.GameObject.Id == pickerId );

		if ( picker == null || picker.IsDead ) return;
		if ( picker.Role != PlayerStats.PlayerRole.Innocent ) return;

		picker.AssignRole( PlayerStats.PlayerRole.Detective );
		picker.GrantWeapon();

		Log.Info( $"{picker.GameObject.Name} подобрал оружие детектива и стал Детективом." );
		GameObject.Destroy();
	}
}
