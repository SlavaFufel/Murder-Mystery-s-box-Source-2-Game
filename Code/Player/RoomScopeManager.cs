using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Scene-level визуальное «рум-куллинг»: отключает рендереры всех GameRoom,
/// в которых сейчас не находится локальный игрок (плюс лобби, если игрок в
/// руме; и наоборот — все румы выключены, когда игрок в хабе).
///
/// Ранее <see cref="GameRoom"/> компонент сам в OnUpdate отключал монеты/
/// регдоллы/прокси-игроков — но геометрия румы (стены, мебель, свет) всегда
/// оставалась включена. При двух занятых румах на клиенте честно рендерилось
/// 4 рума × ~16 моделей и тени для всех сразу. Теперь у клиента в любой
/// момент включена только активная зона (хаб ИЛИ одна из рум).
///
/// Важно: тогглим именно дочерние компоненты, а не GameObject рума целиком —
/// иначе у хоста перестанет тикать <see cref="GameRoom.OnUpdate"/> (state-machine,
/// респаун монет, win-condition).
/// </summary>
public sealed class RoomScopeManager : Component
{
	// Имя GameObject лобби (хаба) в корне сцены.
	[Property] public string LobbyObjectName { get; set; } = "lobby";

	private GameObject _lobby;
	private List<ModelRenderer> _lobbyRenderers;

	// Тир-зона — третий скоуп (помимо хаба и комнат). Включается когда
	// PlayerStats.CurrentRoomId == TirZone.Id.
	private GameObject _tir;
	private List<ModelRenderer> _tirRenderers;
	private System.Guid _tirId;

	// Для каждой румы — её id (GameObject.Id, тот же что PlayerStats.CurrentRoomId)
	// и кешированный список ModelRenderer под её иерархией.
	private struct RoomEntry
	{
		public System.Guid Id;
		public List<ModelRenderer> Renderers;
	}
	private List<RoomEntry> _rooms = new();

	// Чтобы не дёргать .Enabled = ... каждый кадр — пишем только при смене
	// активной румы. System.Guid.Empty означает «активен хаб».
	private System.Guid _lastEnabledRoom = System.Guid.NewGuid(); // заведомо невалидное → форсим первый apply
	private bool _initialized;

	protected override void OnAwake()
	{
		_initialized = false;
	}

	private void EnsureCollected()
	{
		if ( _initialized ) return;
		_initialized = true;

		// Лобби — ищем GameObject по имени среди корневых детей сцены.
		_lobby = null;
		if ( Scene != null )
		{
			foreach ( var go in Scene.GetAllObjects( true ) )
			{
				if ( go == null ) continue;
				if ( !string.Equals( go.Name, LobbyObjectName, System.StringComparison.OrdinalIgnoreCase ) ) continue;
				_lobby = go;
				break;
			}
		}
		_lobbyRenderers = _lobby != null
			? _lobby.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ).ToList()
			: new List<ModelRenderer>();

		// Тир — берём по компоненту TirZone (singleton).
		var tirZone = Scene.GetAllComponents<TirZone>().FirstOrDefault();
		_tir = tirZone?.GameObject;
		_tirId = tirZone?.GameObject?.Id ?? System.Guid.Empty;
		_tirRenderers = _tir != null
			? _tir.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ).ToList()
			: new List<ModelRenderer>();

		// Все румы — берём по компоненту GameRoom.
		_rooms.Clear();
		foreach ( var gr in Scene.GetAllComponents<GameRoom>() )
		{
			if ( gr == null || gr.GameObject == null ) continue;
			var rends = gr.GameObject.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ).ToList();
			_rooms.Add( new RoomEntry { Id = gr.GameObject.Id, Renderers = rends } );
		}
	}

	protected override void OnUpdate()
	{
		EnsureCollected();

		var local = GameManager.LocalRoomId; // Empty = хаб.
		if ( local == _lastEnabledRoom ) return;
		_lastEnabledRoom = local;

		bool inHub = local == System.Guid.Empty;
		bool inTir = _tirId != System.Guid.Empty && local == _tirId;

		// Лобби видно только в хабе.
		SetEnabled( _lobbyRenderers, inHub );

		// Тир видим только когда игрок именно в тир-скоупе.
		SetEnabled( _tirRenderers, inTir );

		// Из всех рум включаем только текущую (если она вообще есть).
		// В хабе и тире все румы выключены.
		foreach ( var r in _rooms )
		{
			SetEnabled( r.Renderers, !inHub && !inTir && r.Id == local );
		}
	}

	private static void SetEnabled( List<ModelRenderer> list, bool enabled )
	{
		if ( list == null ) return;
		foreach ( var r in list )
		{
			if ( r == null || !r.IsValid ) continue;
			if ( r.Enabled != enabled ) r.Enabled = enabled;
		}
	}
}
