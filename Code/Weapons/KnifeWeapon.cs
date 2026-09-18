using Sandbox;
using Sandbox.Citizen;
using System.Linq;

public sealed class KnifeWeapon : WeaponBase
{
	[Property] public float Range { get; set; } = 120f;
	[Property, Group("Sounds")] public string SwingSound { get; set; } = "";
	[Property, Group("Sounds")] public string HitSound { get; set; } = "";

	private PlayerStats _stats;
	private PlayerInventory _inventory;

	protected override void OnStart()
	{
		// Совпадает с MurdererAttackCooldown в PlayerStats — теперь оба «слышат»
		// одно значение, и UI-полоска перезарядки показывает один и тот же таймер.
		_stats = Components.Get<PlayerStats>( FindMode.EverythingInSelfAndAncestors );
		_inventory = Components.Get<PlayerInventory>( FindMode.EverythingInSelfAndAncestors );
		Cooldown = _stats?.MurdererAttackCooldown ?? 5f;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( IsProxy ) return;
		if ( _stats == null || _stats.IsDead ) return;
		if ( _stats.Role != PlayerStats.PlayerRole.Murderer ) return;
		if ( _inventory != null && _inventory.ActiveSlot != PlayerInventory.SlotWeapon ) return;

		// Подхватываем актуальное значение из инспектора (на случай если поправили в рантайме).
		Cooldown = _stats.MurdererAttackCooldown;

		// Блокируем атаку пока Убийца не отдышался после истощения стамины.
		if ( _stats.StaminaAttackBlocked ) return;

		if ( Input.Pressed( "attack1" ) )
		{
			if ( TryAttack() )
				_stats.TriggerMurdererCooldown();
		}
	}

	protected override bool PerformAttack()
	{
		if ( _stats == null || _stats.Role != PlayerStats.PlayerRole.Murderer )
			return false;

		var pos = _stats.GameObject.WorldPosition;

		// Анимация + звук замаха — слышат все клиенты.
		BroadcastSwingEffect( pos );

		var cam = Scene.Camera;
		if ( cam == null ) return true;

		var ray = cam.ScreenNormalToRay( 0.5f );
		var tr = Scene.Trace.Ray( ray, Range )
			.UseHitboxes()
			.IgnoreGameObjectHierarchy( _stats.GameObject )
			.Run();

		if ( !tr.Hit ) return true;

		var target = tr.GameObject?.Root?.GetComponent<PlayerStats>();
		if ( target == null || target.IsDead ) return true;

		// Попадание — звук удара.
		if ( !string.IsNullOrEmpty( HitSound ) )
			BroadcastHitEffect( tr.HitPosition );

		target.Kill();
		_stats?.IncrementStat( "kills", 1 );
		return true;
	}

	[Rpc.Broadcast]
	private void BroadcastSwingEffect( Vector3 pos )
	{
		// Дефолтная s&box-анимация удара. Citizen animgraph слушает b_attack
		// (bool, self-resets через кадр) и holdtype_attack (float — индекс
		// варианта атаки). Для HoldType=Swing (выставляется HeldItemPose) есть
		// единственный вариант 2H melee attack, поэтому holdtype_attack=0.
		// HoldType=Swing уже стоит, т.к. HeldItemPose обновляет его каждый кадр.
		var root = GameObject.Root ?? GameObject;
		var body = root.Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		if ( body != null )
		{
			try { body.Set( "holdtype_attack", 0 ); } catch { }
			try { body.Set( "b_attack", true ); } catch { }
		}

		if ( !string.IsNullOrEmpty( SwingSound ) )
			Sfx.Play( SwingSound, pos, 6f );
	}

	[Rpc.Broadcast]
	private void BroadcastHitEffect( Vector3 pos )
	{
		if ( !string.IsNullOrEmpty( HitSound ) )
			Sfx.Play( HitSound, pos, 6f );
	}
}
