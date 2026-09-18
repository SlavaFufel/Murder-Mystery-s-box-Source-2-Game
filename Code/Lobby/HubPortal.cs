using Sandbox;
using System.Linq;

/// <summary>
/// Портал в хабе. При входе игрока в триггер — показывает меню выбора комнаты (HubPortalUI).
/// Один портал на весь хаб; меню показывает все GameRoom на сцене.
/// </summary>
public sealed class HubPortal : Component, Component.ITriggerListener
{
	private readonly System.Collections.Generic.HashSet<PlayerStats> _inside = new();

	public new static HubPortal Active { get; private set; }

	protected override void OnUpdate()
	{
		var local = Scene.GetAllComponents<PlayerStats>().FirstOrDefault( x => !x.IsProxy );
		bool localInside = local != null && _inside.Contains( local );

		if ( localInside ) Active = this;
		else if ( Active == this ) Active = null;

		if ( !Networking.IsHost ) return;
		_inside.RemoveWhere( p => p == null || !p.IsValid() );
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		var p = other.GameObject.Root.GetComponent<PlayerStats>();
		if ( p != null ) _inside.Add( p );
	}

	void ITriggerListener.OnTriggerExit( Collider other )
	{
		var p = other.GameObject.Root.GetComponent<PlayerStats>();
		if ( p != null ) _inside.Remove( p );
	}
}
