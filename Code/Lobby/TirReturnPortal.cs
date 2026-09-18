using Sandbox;

/// <summary>
/// Триггер внутри тира — телепортирует игрока обратно в хаб.
/// Срабатывает локально у игрока-владельца, он зовёт RPC `RequestTirExit`
/// на хосте. Очередь матчмейкинга НЕ снимается.
/// </summary>
public sealed class TirReturnPortal : Component, Component.ITriggerListener
{
	[Property] public float Cooldown { get; set; } = 0.5f;

	private float _lastRequestTime = 0f;

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		var p = other.GameObject.Root.GetComponent<PlayerStats>();
		if ( p == null || p.IsProxy ) return;

		float now = Time.Now;
		if ( now - _lastRequestTime < Cooldown ) return;
		_lastRequestTime = now;

		p.RequestTirExit();
	}
}
