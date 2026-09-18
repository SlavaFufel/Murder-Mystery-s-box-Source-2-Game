using Sandbox;

/// <summary>
/// Глобальный звук клика по любой UI-кнопке.
/// Запускаемся очень рано (Order = -10000), чтобы Razor-панели не успели
/// «съесть» Input.Pressed до нашей проверки. Курсор виден только когда
/// открыт интерфейс — поэтому в геймплее звук не задевает выстрелы/удары.
/// </summary>
public sealed class UiClickSound : Component
{
	[Property] public string ClickSound { get; set; } = "sounds/tap/tap.sound";

	private bool _wasDown;

	// PreRender вызывается ДО рендера и до большинства PanelComponent-обновлений,
	// что даёт нам шанс поймать клик до того, как Razor «съест» его.
	protected override void OnPreRender()
	{
		if ( string.IsNullOrEmpty( ClickSound ) ) return;
#pragma warning disable CS0612
		if ( !Mouse.Visible ) { _wasDown = false; return; }
#pragma warning restore CS0612

		bool down = Input.Down( "attack1" );
		if ( down && !_wasDown )
			Sfx.Play( ClickSound );
		_wasDown = down;
	}
}
