using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Singleton-маркер тир-зоны. Игрок с CurrentRoomId == TirZone.TirRoomId считается
/// «в тире» — особый скоуп между хабом и комнатами.
///
/// Дополнительно: хост-side per-player target spawning. Мишени-шаблоны
/// (TirTarget с OwnerPlayerId == Empty), расставленные в редакторе, не видны никому.
/// Когда игрок заходит в тир, хост клонирует каждый шаблон и привязывает к его
/// PlayerStats.GameObject.Id — клон виден и реагирует только этому игроку.
/// </summary>
public sealed class TirZone : Component
{
	public static TirZone Instance { get; private set; }
	// Статический helper — возвращает Guid GameObject'а singleton-тира.
	// Назван TirRoomId чтобы не конфликтовать с Component.Id (instance member).
	public static System.Guid TirRoomId => Instance?.GameObject.Id ?? System.Guid.Empty;

	[Property] public GameObject SpawnPoint { get; set; }
	[Property] public GameObject HubReturnPoint { get; set; }
	[Property] public bool GiveKnifeOnEntry { get; set; } = true;

	// Хост-side: GameObject.Id игрока → список клонов мишеней этого игрока.
	// Используем Guid игрока (а не SteamId) — для Instance-ботов SteamId может
	// быть 0, и логика «0 = шаблон» ломалась.
	private readonly Dictionary<System.Guid, List<TirTarget>> _clonesByOwner = new();
	private float _sweepTimer = 0f;

	protected override void OnAwake()
	{
		if ( Instance != null && Instance != this ) return;
		Instance = this;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;

		// Раз в 0.5с: спавнить мишени для новых тир-игроков, удалять для ушедших.
		_sweepTimer += Time.Delta;
		if ( _sweepTimer < 0.5f ) return;
		_sweepTimer = 0f;
		SyncClones();
	}

	private void SyncClones()
	{
		// Кто сейчас в тире?
		var playersInTir = Scene.GetAllComponents<PlayerStats>()
			.Where( p => p != null && p.GameObject != null
				&& p.CurrentRoomId == GameObject.Id )
			.ToList();
		var inTirIds = new HashSet<System.Guid>();
		foreach ( var p in playersInTir )
			inTirIds.Add( p.GameObject.Id );

		// 1. Деспавн клонов чьих владельцев больше нет в тире.
		var toRemove = new List<System.Guid>();
		foreach ( var kv in _clonesByOwner )
		{
			if ( !inTirIds.Contains( kv.Key ) ) toRemove.Add( kv.Key );
		}
		foreach ( var pid in toRemove )
		{
			DespawnFor( pid );
		}

		// 2. Спавн для тех у кого ещё нет клонов.
		foreach ( var p in playersInTir )
		{
			var pid = p.GameObject.Id;
			if ( _clonesByOwner.ContainsKey( pid ) ) continue;
			SpawnFor( p, pid );
		}
	}

	private void SpawnFor( PlayerStats player, System.Guid ownerPlayerId )
	{
		// Шаблоны = все TirTarget'ы в иерархии тира с OwnerPlayerId == Empty.
		var templates = GameObject
			.Components.GetAll<TirTarget>( FindMode.EverythingInDescendants )
			.Where( t => t != null && t.IsValid && t.OwnerPlayerId == System.Guid.Empty )
			.ToList();
		if ( templates.Count == 0 ) return;

		var clones = new List<TirTarget>();
		foreach ( var tmpl in templates )
		{
			if ( tmpl?.GameObject == null ) continue;
			var cloneGo = tmpl.GameObject.Clone( tmpl.GameObject.WorldPosition );
			cloneGo.WorldRotation = tmpl.GameObject.WorldRotation;
			cloneGo.WorldScale    = tmpl.GameObject.WorldScale;
			cloneGo.SetParent( GameObject );

			var cloneTarget = cloneGo.GetComponent<TirTarget>();
			if ( cloneTarget != null )
			{
				cloneTarget.RoomId = GameObject.Id;
				cloneTarget.OwnerPlayerId = ownerPlayerId;
				cloneTarget.Hidden = false;
			}
			cloneGo.NetworkSpawn();
			clones.Add( cloneTarget );
		}
		_clonesByOwner[ownerPlayerId] = clones;
	}

	private void DespawnFor( System.Guid ownerPlayerId )
	{
		if ( !_clonesByOwner.TryGetValue( ownerPlayerId, out var clones ) ) return;
		foreach ( var t in clones )
		{
			if ( t != null && t.IsValid && t.GameObject != null )
				t.GameObject.Destroy();
		}
		_clonesByOwner.Remove( ownerPlayerId );
	}
}
