using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Управляет видимостью предметов в руке игрока. Один набор моделей —
/// видят все, включая самого игрока (он сидит в FP, но смотрит на свою же
/// поднятую руку с предметом, как в Minecraft / Rust / Cs2).
///
/// Модели — дочерние объекты под HeldItems, который через BoneFollower
/// привязан к кости hold_R citizen-модели.
///
/// Для скинов ножа: либо несколько GameObject (ToggleObject), либо один
/// дефолтный с применением Tint к его SkinnedModelRenderer (Tint).
/// </summary>
public sealed class ItemVisibilityController : Component
{
	[System.Serializable]
	public class CosmeticSkin
	{
		[Property] public string Id { get; set; } = "";
		[Property] public GameObject Model { get; set; }
		[Property] public Color TintColor { get; set; } = Color.White;
	}

	[Property] public GameObject KnifeModel { get; set; } // legacy fallback (если KnifeSkins пуст)
	[Property] public GameObject PistolModel { get; set; } // legacy fallback (если GunSkins пуст)
	[Property] public GameObject CoinModel { get; set; }

	// Список вариантов ножа. Заполнить в инспекторе:
	//   knife_default → дефолтная модель ножа
	//   knife_katana  → отдельная модель катаны
	//   knife_golden  → ApplyMethod=Tint в каталоге → Model = дефолтный нож, TintColor = жёлтый
	[Property] public List<CosmeticSkin> KnifeSkins { get; set; } = new();

	// Список вариантов детективского пистолета. Аналогично KnifeSkins:
	//   gun_default     → дефолтная модель
	//   gun_<вариант>   → отдельная модель из дропа
	[Property] public List<CosmeticSkin> GunSkins { get; set; } = new();

	private PlayerInventory _inventory;
	private PlayerStats _stats;

	protected override void OnStart()
	{
		_inventory = Components.Get<PlayerInventory>();
		_stats = Components.Get<PlayerStats>();
	}

	protected override void OnUpdate()
	{
		if ( _inventory == null ) return;

		// Прокси-игрок из чужой румы — не дёргаем модели вообще, прячем всё.
		// Иначе на каждый кадр × 4 румы × N игроков будут лишние SetActive
		// и обходы KnifeSkins.
		if ( _stats != null && _stats.IsProxy && !GameManager.IsInLocalScope( _stats.CurrentRoomId ) )
		{
			SetActiveSafe( KnifeModel, false );
			SetActiveSafe( PistolModel, false );
			SetActiveSafe( CoinModel, false );
			if ( KnifeSkins != null )
				foreach ( var s in KnifeSkins )
					if ( s?.Model != null ) SetActiveSafe( s.Model, false );
			if ( GunSkins != null )
				foreach ( var s in GunSkins )
					if ( s?.Model != null ) SetActiveSafe( s.Model, false );
			return;
		}

		bool dead = _stats != null && _stats.IsDead;
		int slot  = dead ? -1 : _inventory.ActiveSlot;

		bool isMurderer = _stats != null && _stats.Role == PlayerStats.PlayerRole.Murderer;
		bool hasGun     = _stats != null && (_stats.Role == PlayerStats.PlayerRole.Detective || _stats.HasWeapon);
		bool hasCoins   = _stats != null && _stats.Coins > 0;

		bool slot1 = slot == PlayerInventory.SlotWeapon;
		bool slot2 = slot == PlayerInventory.SlotCoins;

		bool showKnife  = !dead && slot1 && isMurderer;
		bool showPistol = !dead && slot1 && !isMurderer && hasGun;
		bool showCoin   = !dead && slot2 && hasCoins;

		ApplyKnifeSkins( showKnife );
		ApplyGunSkins( showPistol );
		SetActiveSafe( CoinModel,   showCoin );
	}

	// Лог печатаем максимум раз в секунду на каждый «не найденный» Id, чтобы
	// не спамить консоль каждый кадр.
	private string _lastWarnedEquipped = null;
	private float _lastWarnTime = -10f;

	private static string NormId( string s ) => (s ?? "").Trim().ToLowerInvariant();

	private void ApplyKnifeSkins( bool show )
	{
		// Legacy: если список скинов не заполнен — просто toggle одного KnifeModel.
		if ( KnifeSkins == null || KnifeSkins.Count == 0 )
		{
			SetActiveSafe( KnifeModel, show );
			return;
		}

		string equippedRaw = _stats?.GetEquipped( "knife" );
		if ( string.IsNullOrEmpty( equippedRaw ) )
			equippedRaw = CosmeticCatalog.DefaultIdForSlot( CosmeticCatalog.Slot.Knife );
		string equippedId = NormId( equippedRaw );
		var item = CosmeticCatalog.Get( equippedRaw );

		// Tint-метод: показываем дефолтную модель и красим её.
		if ( show && item != null && item.Method == CosmeticCatalog.ApplyMethod.Tint )
		{
			var defaultSkin = KnifeSkins.FirstOrDefault( s => NormId( s?.Id ) == "knife_default" )
			                 ?? KnifeSkins.FirstOrDefault();
			Color tint = ParseHex( item.ResourceRef, Color.White );

			foreach ( var s in KnifeSkins )
			{
				if ( s == null || s.Model == null ) continue;
				bool isDefault = s == defaultSkin;
				SetActiveSafe( s.Model, isDefault );
				if ( isDefault ) ApplyTint( s.Model, tint );
			}
			SetActiveSafe( KnifeModel, false ); // legacy не показываем поверх
			return;
		}

		// ToggleObject: показываем только модель с совпадающим Id.
		bool anyMatched = false;
		GameObject activeSkinModel = null;
		foreach ( var s in KnifeSkins )
		{
			if ( s == null || s.Model == null ) continue;
			bool match = show && NormId( s.Id ) == equippedId;
			SetActiveSafe( s.Model, match );
			if ( match ) { ApplyTint( s.Model, s.TintColor ); anyMatched = true; activeSkinModel = s.Model; }
		}

		// Страховка: если для дефолтного ножа не нашлось записи в KnifeSkins
		// (Id не заполнен в префабе) — показываем legacy KnifeModel, чтобы
		// игрок не остался с пустой рукой. Для остальных id не подменяем —
		// иначе будет видно дефолтную модель вместо отсутствующего скина.
		bool useLegacyFallback = show && !anyMatched
			&& equippedId == NormId( CosmeticCatalog.DefaultIdForSlot( CosmeticCatalog.Slot.Knife ) );

		// Внимание: legacy-поле KnifeModel может указывать на тот же GameObject,
		// что и одна из записей KnifeSkins (так часто настраивают для
		// knife_default). В этом случае НЕ трогаем — иначе только что
		// включённую в цикле модель тут же выключим.
		if ( KnifeModel != null && KnifeModel != activeSkinModel )
			SetActiveSafe( KnifeModel, useLegacyFallback );

		// Диагностика: ничего не зажглось при show=true → виновата либо опечатка
		// в Id записи KnifeSkins, либо отсутствие записи под этот скин.
		if ( show && !anyMatched && _stats != null && !_stats.IsProxy )
		{
			if ( _lastWarnedEquipped != equippedId || Time.Now - _lastWarnTime > 3f )
			{
				_lastWarnedEquipped = equippedId;
				_lastWarnTime = Time.Now;
				var have = string.Join( ", ", KnifeSkins.Where( x => x != null ).Select( x => $"'{x.Id}'" ) );
				Log.Warning( $"[ItemVisibilityController] нет скина под equipped='{equippedRaw}' (norm='{equippedId}'). В KnifeSkins есть: [{have}]" );
			}
		}
	}

	// Зеркало ApplyKnifeSkins для слота "gun".
	private string _lastWarnedEquippedGun = null;
	private float _lastWarnTimeGun = -10f;

	private void ApplyGunSkins( bool show )
	{
		string equippedRaw = _stats?.GetEquipped( "gun" );
		if ( string.IsNullOrEmpty( equippedRaw ) )
			equippedRaw = CosmeticCatalog.DefaultIdForSlot( CosmeticCatalog.Slot.Gun );
		string equippedId = NormId( equippedRaw );
		var item = CosmeticCatalog.Get( equippedRaw );

		// Legacy: если список скинов не заполнен — toggle одного PistolModel.
		// Если у игрока экипирован не-дефолтный пистолет — кричим в консоль,
		// иначе пользователь не поймёт почему модель не меняется.
		if ( GunSkins == null || GunSkins.Count == 0 )
		{
			SetActiveSafe( PistolModel, show );
			if ( show && equippedId != NormId( CosmeticCatalog.DefaultIdForSlot( CosmeticCatalog.Slot.Gun ) )
			    && _stats != null && !_stats.IsProxy )
			{
				if ( _lastWarnedEquippedGun != equippedId || Time.Now - _lastWarnTimeGun > 3f )
				{
					_lastWarnedEquippedGun = equippedId;
					_lastWarnTimeGun = Time.Now;
					Log.Warning( $"[ItemVisibilityController] equipped='{equippedRaw}', но GunSkins пуст в Player префабе. Добавь записи в Item Visibility Controller → Gun Skins." );
				}
			}
			return;
		}

		// Tint-метод: показываем дефолтную модель и красим её.
		if ( show && item != null && item.Method == CosmeticCatalog.ApplyMethod.Tint )
		{
			var defaultSkin = GunSkins.FirstOrDefault( s => NormId( s?.Id ) == "gun_default" )
			                 ?? GunSkins.FirstOrDefault();
			Color tint = ParseHex( item.ResourceRef, Color.White );

			foreach ( var s in GunSkins )
			{
				if ( s == null || s.Model == null ) continue;
				bool isDefault = s == defaultSkin;
				SetActiveSafe( s.Model, isDefault );
				if ( isDefault ) ApplyTint( s.Model, tint );
			}
			SetActiveSafe( PistolModel, false );
			return;
		}

		// ToggleObject: показываем только модель с совпадающим Id.
		bool anyMatched = false;
		GameObject activeSkinModel = null;
		foreach ( var s in GunSkins )
		{
			if ( s == null || s.Model == null ) continue;
			bool match = show && NormId( s.Id ) == equippedId;
			SetActiveSafe( s.Model, match );
			if ( match ) { ApplyTint( s.Model, s.TintColor ); anyMatched = true; activeSkinModel = s.Model; }
		}

		// Страховка: если для gun_default не нашлось записи — показываем legacy PistolModel.
		bool useLegacyFallback = show && !anyMatched
			&& equippedId == NormId( CosmeticCatalog.DefaultIdForSlot( CosmeticCatalog.Slot.Gun ) );

		if ( PistolModel != null && PistolModel != activeSkinModel )
			SetActiveSafe( PistolModel, useLegacyFallback );

		if ( show && !anyMatched && _stats != null && !_stats.IsProxy )
		{
			if ( _lastWarnedEquippedGun != equippedId || Time.Now - _lastWarnTimeGun > 3f )
			{
				_lastWarnedEquippedGun = equippedId;
				_lastWarnTimeGun = Time.Now;
				var have = string.Join( ", ", GunSkins.Where( x => x != null ).Select( x => $"'{x.Id}'" ) );
				Log.Warning( $"[ItemVisibilityController] нет скина под equipped='{equippedRaw}' (norm='{equippedId}'). В GunSkins есть: [{have}]" );
			}
		}
	}

	private static void ApplyTint( GameObject go, Color tint )
	{
		var smr = go.Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		if ( smr != null ) { try { smr.Tint = tint; } catch { } }
		var mr = go.Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		if ( mr != null ) { try { mr.Tint = tint; } catch { } }
	}

	private static Color ParseHex( string hex, Color fallback )
	{
		if ( string.IsNullOrEmpty( hex ) ) return fallback;
		try { return Color.Parse( hex ) ?? fallback; } catch { return fallback; }
	}

	private static void SetActiveSafe( GameObject go, bool enabled )
	{
		if ( go == null ) return;
		if ( go.Enabled != enabled ) go.Enabled = enabled;
	}
}
