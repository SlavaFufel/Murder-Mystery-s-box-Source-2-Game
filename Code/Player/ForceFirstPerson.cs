using Sandbox;

/// <summary>
/// Жёстко принуждает Sandbox.PlayerController оставаться в режиме первого лица.
/// Перехватываем нажатие "view" (биндится по умолчанию на C в s&amp;box), и
/// каждый кадр сбрасываем флаг ThirdPerson, на случай если что-то его выставило.
/// Также ставит игрока на паузу пока открыто кастомное Esc-меню.
///
/// Прицепи компонент к тому же GameObject, где лежит Sandbox.PlayerController
/// (обычно это Player prefab).
/// </summary>
public sealed class ForceFirstPerson : Component
{
	[Property] public string ViewToggleAction { get; set; } = "view";

	private Sandbox.PlayerController _controller;

	// Pause: при открытии меню блокируем ТОЛЬКО input (look + move) через
	// UseInputControls. Физика/гравитация продолжают работать — если игрок был
	// в полёте, он падает дальше, как ожидает пользователь. EyeAngles снэпшотим
	// чтобы accumulated mouse delta не дёрнула камеру при возврате из меню.
	private Angles _pausedEyeAngles;
	private bool _wasPaused = false;

	protected override void OnStart()
	{
		_controller = Components.Get<Sandbox.PlayerController>();
		if ( _controller == null )
		{
			Log.Warning( "[ForceFirstPerson] На объекте нет Sandbox.PlayerController — компонент не будет работать." );
			return;
		}

		ApplyFirstPerson();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || _controller == null ) return;

		bool menuOpen = MenuState.SettingsOpen;

		// Транзишн pause → resume или resume → pause.
		if ( menuOpen && !_wasPaused )
		{
			// Только что встали на паузу — снимаем снимок вью.
			try { _pausedEyeAngles = _controller.EyeAngles; } catch { }
		}
		else if ( !menuOpen && _wasPaused )
		{
			// Только что вернулись из меню — принудительно ставим вью на снимок
			// ДО того как контроллер прочтёт следующую mouse-delta. Иначе
			// камера дёргается на накопленный за паузу поворот мыши.
			try { _controller.EyeAngles = _pausedEyeAngles; } catch { }
		}
		_wasPaused = menuOpen;

		if ( menuOpen )
		{
			// Пока меню открыто — держим вью зафиксированным, на случай если
			// что-то всё-таки попыталось крутить камеру.
			try { _controller.EyeAngles = _pausedEyeAngles; } catch { }
			// UseInputControls в некоторых версиях движка не полностью режет
			// движение (если W был зажат до паузы), поэтому страхуемся —
			// принудительно зануляем WishVelocity каждый кадр.
			try { _controller.WishVelocity = Vector3.Zero; } catch { }
		}

		// Блокируем только input — не Enabled. Физика (гравитация, инерция,
		// падение) продолжает работать, как в multiplayer-паузе Майнкрафта.
		try
		{
			if ( _controller.UseInputControls == menuOpen )
				_controller.UseInputControls = !menuOpen;
		}
		catch { /* свойство может отсутствовать в некоторых версиях движка */ }

		// Если игрок (или сторонний код) попытался включить третий вид — мгновенно вернуть.
		if ( _controller.ThirdPerson )
			ApplyFirstPerson();

		// На всякий случай «съедаем» нажатие view-toggle, чтобы оно не сработало
		// нигде ещё (например в Razor UI или другом компоненте).
		if ( !string.IsNullOrEmpty( ViewToggleAction ) && Input.Pressed( ViewToggleAction ) )
			ApplyFirstPerson();
	}

	private void ApplyFirstPerson()
	{
		if ( _controller == null ) return;
		_controller.ThirdPerson = false;
	}
}
