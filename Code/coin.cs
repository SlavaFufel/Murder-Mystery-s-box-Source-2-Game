using Sandbox;
using System.Linq;

public sealed class Coin : Component, Component.ITriggerListener
{
	[Property] public string PickupSound { get; set; } = "";

	[Property, Group( "Idle Animation" )] public float SpinSpeed { get; set; } = 180f; // градусов/сек вокруг Z
	[Property, Group( "Idle Animation" )] public float BobAmplitude { get; set; } = 4f; // юнитов вверх/вниз
	[Property, Group( "Idle Animation" )] public float BobSpeed { get; set; } = 2f;     // циклов/сек

	// К какой GameRoom принадлежит эта монета. Выставляется хостом перед
	// NetworkSpawn (см. GameRoom.SpawnCoinsAtPoints). По нему клиент решает,
	// крутить ли idle-анимацию и держать ли видимым рендер: для монет чужой
	// румы оба отключены — иначе у тебя в кадре крутятся 60 монет четырёх рум.
	[Sync] public System.Guid RoomId { get; set; } = System.Guid.Empty;

	private bool _isCollected = false;
	private Vector3 _basePosition;
	private float _phase;
	private bool _baseCaptured = false;
	private bool? _lastInScope;
	private System.Collections.Generic.List<ModelRenderer> _cachedRenderers;

	protected override void OnStart()
	{
		_basePosition = GameObject.LocalPosition;
		_phase = (float)(Game.Random.Float() * System.Math.PI * 2.0); // чтобы пачка монет не «дышала» в унисон
		_baseCaptured = true;
	}

	protected override void OnUpdate()
	{
		if ( !_baseCaptured ) return;

		bool inScope = GameManager.IsInLocalScope( RoomId );

		// Видимость переключаем только при смене состояния — иначе таскать
		// сотни write'ов в Renderer.Enabled каждый кадр контрпродуктивно.
		if ( _lastInScope != inScope )
		{
			_lastInScope = inScope;
			if ( _cachedRenderers == null )
				_cachedRenderers = Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ).ToList();
			foreach ( var r in _cachedRenderers )
				if ( r != null && r.IsValid && r.Enabled != inScope ) r.Enabled = inScope;
		}

		// В чужой руме монету не анимируем (sin/cos + transform write × N монет).
		if ( !inScope ) return;

		float t = Time.Now * BobSpeed * (float)System.Math.PI * 2f + _phase;
		float dz = (float)System.Math.Sin( t ) * BobAmplitude;

		GameObject.LocalPosition = _basePosition + new Vector3( 0, 0, dz );
		GameObject.LocalRotation = Rotation.FromYaw( Time.Now * SpinSpeed );
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( _isCollected ) return;
		if ( !Networking.IsHost ) return;

		var player = other.GameObject.Root.GetComponent<PlayerStats>();
		if ( player == null || player.IsDead ) return;

		_isCollected = true;
		player.CollectCoin();

		// Звук слышат все — broadcast до Destroy.
		// ВАЖНО: имя звука передаём параметром. К моменту прихода broadcast'а
		// на удалённых клиентах GameObject монетки уже может быть уничтожен,
		// и обращение к this.PickupSound даст null/пустую строку.
		if ( !string.IsNullOrEmpty( PickupSound ) )
			BroadcastPickupSound( GameObject.WorldPosition, PickupSound );

		Log.Info( $"Монета собрана игроком {player.GameObject.Name}" );
		GameObject.Destroy();
	}

	void ITriggerListener.OnTriggerExit( Collider other ) { }

	[Rpc.Broadcast]
	private void BroadcastPickupSound( Vector3 pos, string soundName )
	{
		if ( !string.IsNullOrEmpty( soundName ) )
			Sound.Play( soundName, pos );
	}
}
