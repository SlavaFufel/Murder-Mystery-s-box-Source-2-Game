using Sandbox;

public abstract class WeaponBase : Component
{
	[Property] public float Cooldown { get; set; } = 0.8f;

	protected float CooldownLeft;
	public bool IsReady => CooldownLeft <= 0f;

	protected override void OnUpdate()
	{
		if ( CooldownLeft > 0f )
			CooldownLeft -= Time.Delta;
	}

	public bool TryAttack()
	{
		if ( !IsReady ) return false;
		bool acted = PerformAttack();
		CooldownLeft = Cooldown;
		return acted;
	}

	protected abstract bool PerformAttack();
}
