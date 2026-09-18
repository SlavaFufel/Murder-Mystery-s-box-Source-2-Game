using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Триггер в хабе, который кидает игрока в тир. Триггер срабатывает ЛОКАЛЬНО
/// у игрока-владельца (не на хосте), и игрок сам зовёт RPC `RequestTirEntry`
/// на хосте. Это тот же паттерн что у `RequestRoomEntry`, и он корректно
/// работает для прокси-игроков (включая ботов / второй instance).
/// </summary>
public sealed class TirEntryPortal : Component, Component.ITriggerListener
{
	[Property] public float Cooldown { get; set; } = 0.5f;

	// Локальный анти-bounce: чтоб не дёргать RPC бесконечно при шаге назад
	// внутрь триггера. Это поле не синкается — у каждого клиента своё.
	private float _lastRequestTime = 0f;

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		var p = other.GameObject.Root.GetComponent<PlayerStats>();
		// Срабатываем только для локального игрока — прокси игнорируем.
		// Каждый клиент сам шлёт RPC за себя.
		if ( p == null || p.IsProxy ) return;

		var tir = TirZone.Instance;
		if ( tir == null || tir.SpawnPoint == null ) return;

		float now = Time.Now;
		if ( now - _lastRequestTime < Cooldown ) return;
		_lastRequestTime = now;

		p.RequestTirEntry();
	}
}
