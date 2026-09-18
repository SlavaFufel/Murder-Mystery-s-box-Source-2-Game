using Sandbox;
using System.Linq;

/// <summary>
/// Принудительно ставит Tint на ModelRenderer портала при старте каждого
/// клиента. Существует потому что иногда scene/prefab override Tint не
/// применяется на не-host инстансах и портал выглядит opaque вместо
/// полупрозрачного.
///
/// Использование: добавь этот компонент на тот же GameObject где лежит
/// ModelRenderer портала (Portal_GameRoom_X). В инспекторе укажи нужный
/// Tint — он применится у всех игроков идентично.
/// </summary>
public sealed class PortalAppearance : Component
{
	// Совпадает с #EA00FF68 из инспектора — пурпурный, alpha ~0.41.
	[Property] public Color PortalTint { get; set; } = new Color( 0.92f, 0f, 1f, 0.41f );

	protected override void OnAwake()
	{
		Apply();
	}

	protected override void OnStart()
	{
		// Дублируем — иногда model-renderer материал инициализируется позже OnAwake.
		Apply();
	}

	private float _reapplyTimer;
	protected override void OnUpdate()
	{
		_reapplyTimer -= Time.Delta;
		if ( _reapplyTimer > 0f ) return;
		_reapplyTimer = 0.5f;
		Apply();
	}

	private void Apply()
	{
		var renderers = Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ).ToList();
		if ( renderers.Count == 0 )
		{
			Log.Warning( $"[PortalAppearance] '{GameObject.Name}': ModelRenderer не найден." );
			return;
		}
		foreach ( var r in renderers )
			r.Tint = PortalTint;
		Log.Info( $"[PortalAppearance] '{GameObject.Name}': применено к {renderers.Count} рендерерам. host={Networking.IsHost}" );
	}
}
