using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Крепит модели-аксессуары (крылья, плащи, рюкзаки, нимбы) напрямую к костям
/// citizen-скелета игрока. Альтернатива ClothingContainer — обходит её систему
/// SlotsOver/SlotsUnder, которая не подходит для back-аксессуаров (там нет
/// слота "Back", и Coat/Top конфликтуют с обычной одеждой игрока).
///
/// Как работает:
///   1. Слушает PlayerStats.EquippedCosmeticsSerialized.
///   2. При изменении — собирает все экипированные предметы где Method == Accessory.
///   3. Для каждого создаёт дочерний GameObject под bone'ом из item.AccessoryBone
///      (например "spine_2"), вешает на него ModelRenderer с моделью и применяет
///      LocalPosition / LocalRotation / LocalScale.
///   4. Старые аксессуары удаляются перед пересборкой.
///
/// Crucially: модели парентятся ПОД bone'ом citizen-скелета. Они автоматически
/// следуют за анимацией позвоночника без BoneFollower'а — потому что bone GO
/// сам уже двигается per-frame SkinnedModelRenderer'ом.
///
/// Где регистрировать аксессуары: через CustomAccessoryComponent в инспекторе
/// (drag-drop модели + указать кость + offset/rotation), или вручную в коде:
///   var item = new CosmeticCatalog.Item {
///       Id = "wings_devil", Slot = Slot.Back, Method = ApplyMethod.Accessory,
///       AccessoryModelPath = "models/wings/wings.vmdl",
///       AccessoryBone = "spine_2",
///       AccessoryLocalPosition = new Vector3(-5, 0, 0),
///   };
/// </summary>
public sealed class AccessoryController : Component
{
	// Тег для пометки spawned-аксессуаров. Используется в Rebuild чтобы найти
	// и удалить старые перед созданием новых. Уникальный чтобы не пересекаться
	// с тегом "clothing" который использует ClothingContainer.
	private const string AccessoryTag = "mm-accessory";

	private PlayerStats _stats;
	private SkinnedModelRenderer _bodyRenderer;
	private string _lastAppliedEquipped = null;

	// Список заспавненных GameObject'ов — для надёжной очистки на Rebuild.
	private readonly List<GameObject> _spawned = new();

	// Маппинг id-косметики → заспавненный GameObject. Используется для live-edit
	// transform'ов из инспектора без полной пересборки сцены.
	private readonly Dictionary<string, GameObject> _spawnedById = new();
	// Запомнили имя кости с которой связан каждый заспавненный объект — если в
	// инспекторе сменили AttachBone, нужно сделать полный Rebuild (перепарентить).
	private readonly Dictionary<string, string> _boneById = new();

	protected override void OnStart()
	{
		_stats = Components.Get<PlayerStats>();
	}

	protected override void OnUpdate()
	{
		if ( _stats == null ) return;

		// Прокси из чужой румы — не делаем ничего (как у других контроллеров).
		if ( _stats.IsProxy && !GameManager.IsInLocalScope( _stats.CurrentRoomId ) )
			return;

		// 1) Полный Rebuild при изменении [Sync] поля. Сравниваем именно сырой
		// [Sync] (не Effective с override'ом), иначе после RPC-driven Rebuild
		// этот watch начинает спорить с самим собой:
		//   - RPC arrives, override=NEW, Rebuild reads NEW. Override cleared.
		//   - Next OnUpdate: effective = [Sync] (stale OLD), last = NEW
		//     → mismatch → Rebuild → reads stale [Sync] → re-attaches old items.
		// При сравнении raw [Sync] этот «прыжок» не срабатывает — мы видим
		// [Sync] которое не менялось, ждём пока оно догонит NEW, и только
		// тогда дёрнем Rebuild (который применит уже консистентное значение).
		var rawSync = _stats.EquippedCosmeticsSerialized ?? "";
		if ( rawSync != _lastAppliedEquipped )
		{
			_lastAppliedEquipped = rawSync;
			Rebuild();
			return;
		}

		// 2) Live-edit: каждый кадр читаем transform из catalog и применяем
		//    к заспавненным GO. Если кто-то поменял AttachBone в инспекторе —
		//    делаем полный Rebuild (перепарентить под другую кость).
		LiveSyncTransforms();
	}

	/// <summary>
	/// Дёшево обновляет LocalPosition/Rotation/Scale заспавненных аксессуаров
	/// из текущих значений в каталоге. Нужно для live-настройки в инспекторе.
	/// </summary>
	private void LiveSyncTransforms()
	{
		if ( _spawnedById.Count == 0 ) return;
		bool needRebuild = false;

		foreach ( var kv in _spawnedById )
		{
			var id = kv.Key;
			var go = kv.Value;
			if ( go == null || !go.IsValid ) { needRebuild = true; break; }
			var item = CosmeticCatalog.Get( id );
			if ( item == null || item.Method != CosmeticCatalog.ApplyMethod.Accessory ) continue;

			// Если в инспекторе сменили кость — нужен полный Rebuild чтобы
			// сменить парента.
			var wantBone = string.IsNullOrEmpty( item.AccessoryBone ) ? "spine_2" : item.AccessoryBone;
			if ( !_boneById.TryGetValue( id, out var curBone ) || curBone != wantBone )
			{
				needRebuild = true;
				break;
			}

			// Дешёвый апдейт transform'а каждый кадр. Не пересоздаёт GO.
			go.LocalPosition = item.AccessoryLocalPosition;
			go.LocalRotation = Rotation.From( item.AccessoryLocalRotation );
			var s = item.AccessoryLocalScale > 0 ? item.AccessoryLocalScale : 1f;
			go.LocalScale = new Vector3( s, s, s );
		}

		if ( needRebuild ) Rebuild();
	}

	/// <summary>
	/// Пересобирает все прикреплённые аксессуары. Вызывается автоматически
	/// при изменении экипировки. Так же может быть позвана извне (из
	/// PlayerStats.ApplyClothing), если body SMR пересоздался и старые
	/// аксессуары пропали вместе с дочерними костями.
	/// </summary>
	public void Rebuild()
	{
		if ( _stats == null ) _stats = Components.Get<PlayerStats>();
		if ( _stats == null ) return;

		_bodyRenderer = Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		if ( _bodyRenderer == null )
		{
			Log.Warning( $"[ACCESSORY] Rebuild: no body renderer found on player={_stats.DisplayName}" );
			return;
		}

		// Читаем актуальное состояние через override-aware getter — на прокси
		// внутри BroadcastRefreshClothingForProxies override содержит свежее
		// значение, [Sync] может ещё не прорасти.
		var equippedStr = _stats.GetEffectiveEquippedSerialized() ?? "";

		// Синхронизируем watch с тем что только что применили — иначе следующий
		// OnUpdate увидит mismatch со stale [Sync] и снова дёрнет Rebuild,
		// прицепив обратно удалённые предметы.
		_lastAppliedEquipped = _stats.EquippedCosmeticsSerialized ?? "";

		// 1) Снести все ранее заспавненные нами аксессуары.
		foreach ( var g in _spawned )
		{
			if ( g != null && g.IsValid ) g.Destroy();
		}
		_spawned.Clear();
		_spawnedById.Clear();
		_boneById.Clear();

		// 2) Найти все экипированные предметы с Method=Accessory и прицепить их.
		var equippedMap = ParseEquipped( equippedStr );
		foreach ( var kv in equippedMap )
		{
			var id = kv.Value;
			if ( string.IsNullOrEmpty( id ) ) continue;
			var item = CosmeticCatalog.Get( id );
			if ( item == null ) continue;
			if ( item.Method != CosmeticCatalog.ApplyMethod.Accessory ) continue;
			AttachOne( item );
		}
	}

	private void AttachOne( CosmeticCatalog.Item item )
	{
		if ( string.IsNullOrEmpty( item.AccessoryModelPath ) )
		{
			Log.Warning( $"[Accessory] '{item.Id}' не задан AccessoryModelPath" );
			return;
		}

		// Получаем bone GameObject у скелета citizen body.
		var boneName = string.IsNullOrEmpty( item.AccessoryBone ) ? "spine_2" : item.AccessoryBone;
		GameObject boneGo = null;
		try { boneGo = _bodyRenderer.GetBoneObject( boneName ); }
		catch { /* старые версии s&box могут не иметь метода — fallthrough */ }

		if ( boneGo == null )
		{
			Log.Warning( $"[Accessory] кость '{boneName}' не найдена у citizen body для '{item.Id}'" );
			return;
		}

		// Загружаем модель из ассетов.
		Model model = null;
		try { model = Model.Load( item.AccessoryModelPath ); }
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Accessory] не загрузилась модель '{item.AccessoryModelPath}' для '{item.Id}': {ex.Message}" );
			return;
		}
		if ( model == null )
		{
			Log.Warning( $"[Accessory] Model.Load вернул null для '{item.AccessoryModelPath}'" );
			return;
		}

		// Создаём дочерний GO под bone'ом. Он будет автоматически следовать
		// за костью per-frame потому что bone GO сам двигается SkinnedModelRenderer'ом.
		var go = new GameObject( true, $"Accessory_{item.Id}" );
		go.SetParent( boneGo, false );
		go.LocalPosition = item.AccessoryLocalPosition;
		go.LocalRotation = Rotation.From( item.AccessoryLocalRotation );
		var s = item.AccessoryLocalScale > 0 ? item.AccessoryLocalScale : 1f;
		go.LocalScale = new Vector3( s, s, s );
		go.Tags.Add( AccessoryTag );

		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = model;

		_spawned.Add( go );
		_spawnedById[item.Id] = go;
		_boneById[item.Id] = boneName;
	}

	private static Dictionary<string, string> ParseEquipped( string serialized )
	{
		var dict = new Dictionary<string, string>();
		if ( string.IsNullOrEmpty( serialized ) ) return dict;
		foreach ( var pair in serialized.Split( ';', System.StringSplitOptions.RemoveEmptyEntries ) )
		{
			var idx = pair.IndexOf( ':' );
			if ( idx <= 0 ) continue;
			dict[pair.Substring( 0, idx )] = pair.Substring( idx + 1 );
		}
		return dict;
	}

	protected override void OnDestroy()
	{
		// Чистим за собой если GameObject игрока разрушается. Bone GO будут
		// уничтожены вместе с body, но _spawned мог содержать ссылки на них.
		foreach ( var g in _spawned )
			if ( g != null && g.IsValid ) g.Destroy();
		_spawned.Clear();
		_spawnedById.Clear();
		_boneById.Clear();
	}
}
